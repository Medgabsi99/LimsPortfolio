using System.Text.Json;
using Lims.Core;

namespace Lims.RestApi.Middleware;

/// <summary>
/// Global exception handler: converts unhandled exceptions into consistent JSON
/// error responses. Keeps controllers free of try/catch noise.
///
/// Mapping rules:
///   LimsBusinessException  → 400 Bad Request  (intentional business rule violation)
///   TimeoutException        → 504 Gateway Timeout
///   Everything else         → 500 Internal Server Error
///
/// Note: InvalidOperationException was previously mapped to 400, which was
/// incorrect — .NET throws it internally for many non-business reasons.
/// Use LimsBusinessException explicitly from domain code for 400 responses.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);

            var (statusCode, title) = ex switch
            {
                LimsBusinessException => (StatusCodes.Status400BadRequest,    "Business rule violation"),
                TimeoutException      => (StatusCodes.Status504GatewayTimeout, "Database timeout"),
                _                    => (StatusCodes.Status500InternalServerError, "Unexpected server error")
            };

            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            var payload = JsonSerializer.Serialize(new
            {
                error  = title,
                detail = ex.Message,
                status = statusCode
            });

            await context.Response.WriteAsync(payload);
        }
    }
}