using System.Globalization;
using System.Text;
using System.Text.Json;
using FarmBasics.Api.Persistence;
using FarmBasics.Api.Platform;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;
using FarmBasics.BuildingBlocks.Observability;
using Npgsql;

namespace FarmBasics.Api.Sync;

public sealed record SyncSpikeRequest(
    Guid ClientGuid,
    string EntityType,
    string PayloadJson,
    DateTimeOffset ClientRecordedAt);

public sealed record SyncSpikeResult(
    Guid RecordId,
    Guid ClientGuid,
    DateTimeOffset ClientRecordedAt,
    DateTimeOffset ServerReceivedAt);

internal static class SyncSpikeEndpoint
{
    private const string RouteTemplate = "POST /v1/sync/spike-records";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapSyncSpikeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/sync/spike-records", HandlePostAsync);
    }

    private static async Task<IResult> HandlePostAsync(
        HttpContext httpContext,
        SyncSpikeRequest request,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        ICurrentIdentityAccessor identityAccessor,
        CancellationToken cancellationToken)
    {
        if (!IdempotencyStore.TryValidateKeyHeader(httpContext.Request, out Guid idempotencyKey))
        {
            return IdempotencyStore.MissingKeyResult(httpContext);
        }

        if (request.ClientGuid == Guid.Empty || string.IsNullOrWhiteSpace(request.EntityType) || string.IsNullOrWhiteSpace(request.PayloadJson))
        {
            return Results.Json(
                new ApiErrorResponse(
                    new ApiErrorDetail("VALIDATION_ERROR", "ClientGuid, EntityType, and PayloadJson are required."),
                    new ApiMeta(httpContext.TraceIdentifier)),
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            using JsonDocument parsed = JsonDocument.Parse(request.PayloadJson);
        }
        catch (JsonException)
        {
            return Results.Json(
                new ApiErrorResponse(
                    new ApiErrorDetail("INVALID_JSON", "PayloadJson must contain valid JSON."),
                    new ApiMeta(httpContext.TraceIdentifier)),
                statusCode: StatusCodes.Status400BadRequest);
        }

        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return Results.Json(
                new ApiErrorResponse(
                    new ApiErrorDetail("SERVICE_UNAVAILABLE", "Database connection is not configured."),
                    new ApiMeta(httpContext.TraceIdentifier)),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        CurrentIdentity? identity = identityAccessor.GetCurrentIdentity(httpContext.User);
        Guid? identityGuid = identity is not null && Guid.TryParse(identity.Subject, out Guid parsedSub)
            ? parsedSub
            : null;

        string fingerprint = ComputeFingerprint(request);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);

        long? lockedRowId = await IdempotencyStore.TryClaimAsync(
            conn,
            tx,
            idempotencyKey,
            identityGuid,
            RouteTemplate,
            fingerprint,
            cancellationToken);

        if (lockedRowId.HasValue)
        {
            Guid recordId = Guid.NewGuid();
            DateTimeOffset serverReceivedAt;

            await using (NpgsqlCommand insertRecordCmd = conn.CreateCommand())
            {
                insertRecordCmd.Transaction = tx;
                insertRecordCmd.CommandText = """
                    INSERT INTO platform.sync_spike_records (id, client_guid, entity_type, payload_json, client_recorded_at, server_received_at)
                    VALUES (@id, @clientGuid, @entityType, CAST(@payloadJson AS jsonb), @clientRecordedAt, now())
                    ON CONFLICT (client_guid) DO NOTHING
                    RETURNING server_received_at;
                    """;
                insertRecordCmd.Parameters.AddWithValue("id", recordId);
                insertRecordCmd.Parameters.AddWithValue("clientGuid", request.ClientGuid);
                insertRecordCmd.Parameters.AddWithValue("entityType", request.EntityType.Trim());
                insertRecordCmd.Parameters.AddWithValue("payloadJson", request.PayloadJson);
                insertRecordCmd.Parameters.AddWithValue("clientRecordedAt", request.ClientRecordedAt.ToUniversalTime());

                object? tsObj = await insertRecordCmd.ExecuteScalarAsync(cancellationToken);
                if (tsObj is null)
                {
                    await tx.RollbackAsync(cancellationToken);
                    PlatformMetrics.PilotSyncOutcomesTotal.WithLabels("conflict").Inc();
                    return Results.Json(
                        new ApiErrorResponse(
                            new ApiErrorDetail("CLIENT_GUID_ALREADY_SYNCED", "This offline entity GUID was already synced under a different Idempotency-Key."),
                            new ApiMeta(httpContext.TraceIdentifier)),
                        statusCode: StatusCodes.Status409Conflict);
                }

                serverReceivedAt = tsObj switch
                {
                    DateTimeOffset dto => dto,
                    DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
                    _ => DateTimeOffset.UtcNow
                };
            }

            ApiResponse<SyncSpikeResult> responseEnvelope = new(
                new SyncSpikeResult(recordId, request.ClientGuid, request.ClientRecordedAt.ToUniversalTime(), serverReceivedAt),
                new ApiMeta(httpContext.TraceIdentifier));
            string serializedBody = JsonSerializer.Serialize(responseEnvelope, JsonOptions);

            await IdempotencyStore.StoreResponseAsync(
                conn,
                tx,
                lockedRowId.Value,
                StatusCodes.Status201Created,
                serializedBody,
                cancellationToken);

            await tx.CommitAsync(cancellationToken);
            PlatformMetrics.PilotSyncOutcomesTotal.WithLabels("succeeded").Inc();
            return Results.Text(serializedBody, "application/json", Encoding.UTF8, StatusCodes.Status201Created);
        }

        await tx.RollbackAsync(cancellationToken);

        IdempotencyLookupResult lookup = await IdempotencyStore.WaitForStoredResponseAsync(
            conn,
            idempotencyKey,
            identityGuid,
            RouteTemplate,
            fingerprint,
            cancellationToken);

        if (lookup.Outcome == IdempotencyLookupOutcome.FingerprintMismatch)
        {
            PlatformMetrics.PilotSyncOutcomesTotal.WithLabels("conflict").Inc();
            return IdempotencyStore.KeyReusedResult(httpContext);
        }

        if (lookup.Outcome == IdempotencyLookupOutcome.Completed && lookup.StatusCode.HasValue && lookup.ResponseBody is not null)
        {
            PlatformMetrics.PilotSyncOutcomesTotal.WithLabels("succeeded").Inc();
            return Results.Text(lookup.ResponseBody, "application/json", Encoding.UTF8, lookup.StatusCode.Value);
        }

        PlatformMetrics.PilotSyncOutcomesTotal.WithLabels("failed").Inc();
        return IdempotencyStore.InFlightTimeoutResult(httpContext);
    }

    private static string ComputeFingerprint(SyncSpikeRequest request)
    {
        string canonical = JsonSerializer.Serialize(new
        {
            request.ClientGuid,
            entityType = request.EntityType.Trim(),
            payloadJson = request.PayloadJson,
            clientRecordedAtUtc = request.ClientRecordedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
        }, JsonOptions);

        return IdempotencyStore.ComputeFingerprint(canonical);
    }
}
