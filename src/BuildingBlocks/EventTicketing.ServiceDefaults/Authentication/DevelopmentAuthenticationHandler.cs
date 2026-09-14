using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EventTicketing.ServiceDefaults.Authentication;

internal sealed class DevelopmentAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "LocalDevelopment";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, "local-test-user"),
            new(ClaimTypes.Name, "local-test-user"),
            new(ClaimTypes.Role, "event-admin"),
            new(ClaimTypes.Role, "ticket-buyer"),
            new(ClaimTypes.Role, "report-reader")
        ];

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, SchemeName)));
    }
}
