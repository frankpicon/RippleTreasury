using Asp.Versioning;
using EventTicketing.ServiceDefaults.Authentication;
using EventTicketing.ServiceDefaults.Correlation;
using EventTicketing.ServiceDefaults.Errors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace EventTicketing.ServiceDefaults;

public static class PlatformDefaultsExtensions
{
    public static WebApplicationBuilder AddPlatformDefaults(
        this WebApplicationBuilder builder,
        string serviceName)
    {
        builder.Host.UseSerilog((context, services, logger) => logger
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Service", serviceName)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", LogEventLevel.Warning)
            .MinimumLevel.Override("Yarp.ReverseProxy", LogEventLevel.Warning)
            .WriteTo.Console()
            .WriteTo.Seq(context.Configuration["Seq:Url"] ?? "http://localhost:5341"));

        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddScoped<ICorrelationContext, HttpCorrelationContext>();

        var allowedOrigins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? ["http://localhost:3000"];

        builder.Services.AddCors(options => options.AddPolicy("web", policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithExposedHeaders(CorrelationIdMiddleware.HeaderName, "Idempotency-Replayed", "api-supported-versions", "api-deprecated-versions")));

        AddAuthentication(builder);
        builder.Services.AddAuthorization();

        builder.Services.AddApiVersioning(options =>
        {
            // Every REST request must carry its version in the URL.
            options.AssumeDefaultVersionWhenUnspecified = false;
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
            options.ReportApiVersions = true;
        }).AddMvc().AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'VVV";
            options.SubstituteApiVersionInUrl = true;
        });
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = serviceName,
                Version = "v1",
                Description = "All REST endpoints require /api/v1 routes. Unversioned URLs return 404. " +
                    "Unsupported URL versions return 404; V2 is not implemented."
            });
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "Paste a Keycloak access token.",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT"
            });
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                }] = []
            });
        });

        var otlpEndpoint = builder.Configuration["OpenTelemetry:Endpoint"];
        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSource("MassTransit");

                if (Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var endpoint))
                {
                    tracing.AddOtlpExporter(options => options.Endpoint = endpoint);
                }
            })
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        _ = telemetry;
        return builder;
    }

    public static WebApplication UsePlatformDefaults(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging(options =>
        {
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                if (httpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemName,
                        out var correlationId))
                {
                    diagnosticContext.Set(CorrelationIdMiddleware.ItemName, correlationId);
                }
            };
        });
        app.UseExceptionHandler();
        app.UseCors("web");
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseSwagger();
        app.UseSwaggerUI();
        return app;
    }

    private static void AddAuthentication(WebApplicationBuilder builder)
    {
        var enabled = builder.Configuration.GetValue("Authentication:Enabled", true);
        if (!enabled)
        {
            builder.Services.AddAuthentication(DevelopmentAuthenticationHandler.SchemeName)
                .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions,
                    DevelopmentAuthenticationHandler>(DevelopmentAuthenticationHandler.SchemeName, _ => { });
            return;
        }

        var authority = builder.Configuration["Authentication:Authority"]
            ?? throw new InvalidOperationException("Authentication:Authority is required.");
        var internalAuthority = builder.Configuration["Authentication:InternalAuthority"] ?? authority;
        var audience = builder.Configuration["Authentication:Audience"] ?? "ticketing-api";

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.RequireHttpsMetadata = builder.Configuration.GetValue(
                    "Authentication:RequireHttpsMetadata", true);
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateIssuer = true,
                    ValidIssuer = authority,
                    NameClaimType = "preferred_username",
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role
                };

                if (!string.Equals(authority, internalAuthority, StringComparison.OrdinalIgnoreCase))
                {
                    options.BackchannelHttpHandler = new BackchannelAddressRewriteHandler(
                        new Uri(authority), new Uri(internalAuthority));
                }

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!Microsoft.Extensions.Primitives.StringValues.IsNullOrEmpty(accessToken) &&
                            context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        builder.Services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation,
            KeycloakRolesClaimsTransformation>();
    }
}
