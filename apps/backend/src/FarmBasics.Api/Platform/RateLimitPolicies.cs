using System.Threading.RateLimiting;
using FarmBasics.BuildingBlocks.Api;
using Microsoft.AspNetCore.RateLimiting;

namespace FarmBasics.Api.Platform;

/// <summary>
/// Application-layer rate limiting policies enforcing Doc/03-ENG §1.8 & §15 and 01A-ENG D-11.
/// </summary>
public static class RateLimitPolicies
{
    public const string PestReportSubmit = "pest-report-submit";
    public const string OtpVerify = "otp-verify";
    public const string AuditSearch = "audit-search";
    public const string OfferAcceptUnthrottled = "offer-accept-unthrottled";

    public const int GlobalPermitLimitPerMinute = 100;
    public const int PestReportLimitPer24Hours = 5;
    public const int OtpVerifyLimitPer15Minutes = 5;
    public const int AuditSearchLimitPerMinute = 30;

    public static string ResolvePartitionKey(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.User.FindFirst("sub")?.Value
               ?? httpContext.Connection.RemoteIpAddress?.ToString()
               ?? "anonymous";
    }

    public static IServiceCollection AddFarmBasicsRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.ContentType = "application/json";
                ApiErrorResponse payload = new(
                    new ApiErrorDetail("RATE_LIMIT_EXCEEDED", "Too many requests. Please wait before trying again."),
                    new ApiMeta(context.HttpContext.TraceIdentifier));
                await context.HttpContext.Response.WriteAsJsonAsync(payload, cancellationToken);
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolvePartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = GlobalPermitLimitPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            options.AddPolicy(PestReportSubmit, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolvePartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = PestReportLimitPer24Hours,
                        Window = TimeSpan.FromHours(24),
                        QueueLimit = 0
                    }));

            options.AddPolicy(OtpVerify, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolvePartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = OtpVerifyLimitPer15Minutes,
                        Window = TimeSpan.FromMinutes(15),
                        QueueLimit = 0
                    }));

            options.AddPolicy(AuditSearch, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    ResolvePartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = AuditSearchLimitPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));

            options.AddPolicy(OfferAcceptUnthrottled, _ =>
                RateLimitPartition.GetNoLimiter("unthrottled"));
        });

        return services;
    }
}
