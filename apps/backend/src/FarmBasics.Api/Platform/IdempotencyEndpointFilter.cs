using System.Text;
using System.Text.Json;
using FarmBasics.Api.Persistence;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;
using Npgsql;

namespace FarmBasics.Api.Platform;

/// <summary>
/// Reusable endpoint filter implementing Idempotency-Key enforcement, replay deduplication,
/// and 409 IDEMPOTENCY_KEY_REUSED detection on POST/PATCH routes (Doc/03-ENG §1.5).
/// </summary>
internal sealed class IdempotencyEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        HttpContext httpContext = context.HttpContext;
        if (!IdempotencyStore.TryValidateKeyHeader(httpContext.Request, out Guid idempotencyKey))
        {
            return IdempotencyStore.MissingKeyResult(httpContext);
        }

        DatabaseConnectionSource dbSource = httpContext.RequestServices.GetRequiredService<DatabaseConnectionSource>();
        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return Results.Json(
                new ApiErrorResponse(
                    new ApiErrorDetail("SERVICE_UNAVAILABLE", "Database connection is not configured."),
                    new ApiMeta(httpContext.TraceIdentifier)),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        SchemaMigrationRunner migrationRunner = httpContext.RequestServices.GetRequiredService<SchemaMigrationRunner>();
        await migrationRunner.EnsureMigratedAsync(dataSource, httpContext.RequestAborted);

        string rawBody = await ReadBodyPreservingStreamAsync(httpContext.Request, httpContext.RequestAborted);
        string routePattern = (httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText
                              ?? httpContext.Request.Path.Value
                              ?? "/";
        string normalizedRoute = $"{httpContext.Request.Method.ToUpperInvariant()} {routePattern}";
        string fingerprint = IdempotencyStore.ComputeFingerprint($"{normalizedRoute}\n{rawBody}");

        ICurrentIdentityAccessor identityAccessor = httpContext.RequestServices.GetRequiredService<ICurrentIdentityAccessor>();
        CurrentIdentity? identity = identityAccessor.GetCurrentIdentity(httpContext.User);
        Guid? identityGuid = identity is not null && Guid.TryParse(identity.Subject, out Guid parsedSub)
            ? parsedSub
            : null;

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(httpContext.RequestAborted);
        long? lockedRowId = await IdempotencyStore.TryClaimAsync(
            conn,
            transaction: null,
            idempotencyKey,
            identityGuid,
            normalizedRoute,
            fingerprint,
            httpContext.RequestAborted);

        if (!lockedRowId.HasValue)
        {
            IdempotencyLookupResult lookup = await IdempotencyStore.WaitForStoredResponseAsync(
                conn,
                idempotencyKey,
                identityGuid,
                normalizedRoute,
                fingerprint,
                httpContext.RequestAborted);

            return lookup.Outcome switch
            {
                IdempotencyLookupOutcome.FingerprintMismatch => IdempotencyStore.KeyReusedResult(httpContext),
                IdempotencyLookupOutcome.Completed when lookup.StatusCode.HasValue && lookup.ResponseBody is not null
                    => Results.Text(lookup.ResponseBody, "application/json", Encoding.UTF8, lookup.StatusCode.Value),
                _ => IdempotencyStore.InFlightTimeoutResult(httpContext)
            };
        }

        try
        {
            object? endpointResult = await next(context);
            (int statusCode, string serializedJson) = await ExecuteAndCaptureJsonAsync(endpointResult, httpContext);

            if (statusCode >= 500)
            {
                await IdempotencyStore.ReleaseUnfinishedLockAsync(dataSource, lockedRowId.Value, CancellationToken.None);
            }
            else
            {
                await IdempotencyStore.StoreResponseAsync(
                    conn,
                    transaction: null,
                    lockedRowId.Value,
                    statusCode,
                    serializedJson,
                    httpContext.RequestAborted);
            }

            return Results.Text(serializedJson, "application/json", Encoding.UTF8, statusCode);
        }
        catch
        {
            await IdempotencyStore.ReleaseUnfinishedLockAsync(dataSource, lockedRowId.Value, CancellationToken.None);
            throw;
        }
    }

    private static async Task<string> ReadBodyPreservingStreamAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        request.Body.Position = 0;
        using StreamReader reader = new(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        string body = await reader.ReadToEndAsync(cancellationToken);
        request.Body.Position = 0;
        return body;
    }

    private static async Task<(int StatusCode, string SerializedJson)> ExecuteAndCaptureJsonAsync(
        object? endpointResult,
        HttpContext outerHttpContext)
    {
        if (endpointResult is not IResult actionResult)
        {
            string serialized = JsonSerializer.Serialize(endpointResult);
            return (StatusCodes.Status200OK, serialized);
        }

        DefaultHttpContext captureContext = new()
        {
            RequestServices = outerHttpContext.RequestServices,
            TraceIdentifier = outerHttpContext.TraceIdentifier
        };
        using MemoryStream buffer = new();
        captureContext.Response.Body = buffer;

        await actionResult.ExecuteAsync(captureContext);

        buffer.Position = 0;
        string bodyText = Encoding.UTF8.GetString(buffer.ToArray());
        if (string.IsNullOrWhiteSpace(bodyText))
        {
            bodyText = JsonSerializer.Serialize(new { statusCode = captureContext.Response.StatusCode });
        }

        return (captureContext.Response.StatusCode, bodyText);
    }
}

internal static class IdempotencyFilterExtensions
{
    public static RouteHandlerBuilder RequireIdempotency(this RouteHandlerBuilder builder)
        => builder.AddEndpointFilter(new IdempotencyEndpointFilter());
}
