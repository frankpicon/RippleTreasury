using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventTicketing.ServiceDefaults.Errors;

internal sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            RequestValidationException => (StatusCodes.Status400BadRequest, "Validation failed", exception.Message),
            ResourceNotFoundException => (StatusCodes.Status404NotFound, "Resource not found", exception.Message),
            ResourceConflictException => (StatusCodes.Status409Conflict, "Request conflict", exception.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Concurrency conflict",
                "The resource changed while this request was being processed. Reload it and try again."),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error",
                "An unexpected error occurred.")
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning(exception, "Request failed with status {StatusCode}", status);
        }

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Instance = httpContext.Request.Path,
                Extensions = { ["traceId"] = httpContext.TraceIdentifier }
            }
        });
    }
}
