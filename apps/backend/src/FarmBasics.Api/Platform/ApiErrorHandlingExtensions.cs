using System.Diagnostics;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Observability;

namespace FarmBasics.Api.Platform;

internal static class ApiErrorHandlingExtensions
{
    public static WebApplication UseFarmBasicsRequestPipeline(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            long startedAt = Stopwatch.GetTimestamp();
            try
            {
                await next();
            }
            catch (BadHttpRequestException)
            {
                await WriteErrorIfNotStartedAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    "VALIDATION_ERROR",
                    "The request body could not be read or contained malformed JSON.");
            }
            catch (Exception ex)
            {
                ILogger<Program> logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                ApiLog.UnhandledException(logger, context.TraceIdentifier, ex);

                await WriteErrorIfNotStartedAsync(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "INTERNAL_ERROR",
                    "An unexpected error occurred. Please try again later.");
            }
            finally
            {
                double elapsedSeconds = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
                string routePattern = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
                                      ?? context.Request.Path.Value
                                      ?? "/";
                string method = context.Request.Method;
                string statusClass = PlatformMetrics.ClassifyStatusCode(context.Response.StatusCode);

                PlatformMetrics.HttpRequestDurationSeconds.WithLabels(method, routePattern).Observe(elapsedSeconds);
                PlatformMetrics.HttpRequestsTotal.WithLabels(method, routePattern, statusClass).Inc();
            }
        });

        app.UseStatusCodePages(async statusCodeContext =>
        {
            HttpContext httpContext = statusCodeContext.HttpContext;
            if (httpContext.Response.HasStarted || !string.IsNullOrEmpty(httpContext.Response.ContentType))
            {
                return;
            }

            (string code, string message) = httpContext.Response.StatusCode switch
            {
                StatusCodes.Status400BadRequest => ("VALIDATION_ERROR", "The request is missing required fields or contains invalid values."),
                StatusCodes.Status401Unauthorized => ("AUTHENTICATION_REQUIRED", "Authentication is required to access this resource."),
                StatusCodes.Status403Forbidden => ("PERMISSION_DENIED", "You do not have permission to perform this action."),
                StatusCodes.Status404NotFound => ("RESOURCE_NOT_FOUND", "The requested resource was not found or is not accessible."),
                StatusCodes.Status405MethodNotAllowed => ("METHOD_NOT_ALLOWED", "This HTTP method is not supported for this endpoint."),
                StatusCodes.Status409Conflict => ("CONFLICT", "The operation could not complete due to a state or uniqueness conflict."),
                StatusCodes.Status422UnprocessableEntity => ("BUSINESS_RULE_VIOLATION", "The request violated a platform business rule."),
                StatusCodes.Status429TooManyRequests => ("RATE_LIMIT_EXCEEDED", "Too many requests. Please wait before trying again."),
                StatusCodes.Status503ServiceUnavailable => ("SERVICE_UNAVAILABLE", "A required service is temporarily unavailable."),
                _ => ("REQUEST_FAILED", "The request could not be completed.")
            };

            httpContext.Response.ContentType = "application/json";
            ApiErrorResponse payload = new(
                new ApiErrorDetail(code, message),
                new ApiMeta(httpContext.TraceIdentifier));
            await httpContext.Response.WriteAsJsonAsync(payload, httpContext.RequestAborted);
        });

        return app;
    }

    private static async Task WriteErrorIfNotStartedAsync(HttpContext context, int statusCode, string code, string message)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        ApiErrorResponse error = new(
            new ApiErrorDetail(code, message),
            new ApiMeta(context.TraceIdentifier));
        await context.Response.WriteAsJsonAsync(error, context.RequestAborted);
    }
}
