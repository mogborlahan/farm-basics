using FarmBasics.Api.Auth;
using FarmBasics.Api.Platform;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Auth;
using Microsoft.AspNetCore.Authorization;
using Npgsql;
using NpgsqlTypes;

namespace FarmBasics.Api.Audit;

public sealed record AuditEntryDto(
    Guid Id,
    string Domain,
    string EventType,
    string EntityType,
    Guid EntityId,
    Guid? ActorId,
    string? PayloadJson,
    DateTimeOffset CreatedAt);

public sealed record EnsurePartitionsResult(IReadOnlyList<string> PartitionsEnsured);

internal static class AuditEndpoints
{
    public static void MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/admin/audit/search", [Authorize] async (
            string? domain,
            string? entityType,
            Guid? actorId,
            int? limit,
            HttpContext httpContext,
            DatabaseConnectionSource dbSource,
            CancellationToken cancellationToken) =>
        {
            NpgsqlDataSource? dataSource = dbSource.DataSource;
            if (dataSource is null)
            {
                return Results.Json(
                    new ApiErrorResponse(
                        new ApiErrorDetail("SERVICE_UNAVAILABLE", "Audit database is not configured."),
                        new ApiMeta(httpContext.TraceIdentifier)),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            int effectiveLimit = Math.Clamp(limit ?? 50, 1, 200);

            await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlCommand cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, domain, event_type, entity_type, entity_id, actor_id, payload::text, created_at
                FROM audit.audit_log_entry
                WHERE (@domain IS NULL OR domain = @domain)
                  AND (@entityType IS NULL OR entity_type = @entityType)
                  AND (@actorId IS NULL OR actor_id = @actorId)
                ORDER BY created_at DESC
                LIMIT @limit;
                """;

            NpgsqlParameter domainParam = cmd.Parameters.Add("domain", NpgsqlDbType.Text);
            domainParam.Value = string.IsNullOrWhiteSpace(domain) ? DBNull.Value : domain.Trim();

            NpgsqlParameter entityParam = cmd.Parameters.Add("entityType", NpgsqlDbType.Text);
            entityParam.Value = string.IsNullOrWhiteSpace(entityType) ? DBNull.Value : entityType.Trim();

            NpgsqlParameter actorParam = cmd.Parameters.Add("actorId", NpgsqlDbType.Uuid);
            actorParam.Value = actorId.HasValue ? actorId.Value : DBNull.Value;

            cmd.Parameters.AddWithValue("limit", effectiveLimit);

            List<AuditEntryDto> items = [];
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new AuditEntryDto(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetGuid(4),
                    reader.IsDBNull(5) ? null : reader.GetGuid(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.GetFieldValue<DateTimeOffset>(7)));
            }

            return Results.Ok(new ApiResponse<IReadOnlyList<AuditEntryDto>>(items, new ApiMeta(httpContext.TraceIdentifier)));
        })
        .RequireAdminPermission("audit", "search")
        .RequireRateLimiting(RateLimitPolicies.AuditSearch);

        app.MapPost("/v1/admin/audit/partitions/ensure", [Authorize] async (
            int? monthsAhead,
            HttpContext httpContext,
            ICurrentIdentityAccessor identityAccessor,
            DatabaseConnectionSource dbSource,
            CancellationToken cancellationToken) =>
        {
            NpgsqlDataSource? dataSource = dbSource.DataSource;
            if (dataSource is null)
            {
                return Results.Json(
                    new ApiErrorResponse(
                        new ApiErrorDetail("SERVICE_UNAVAILABLE", "Audit database is not configured."),
                        new ApiMeta(httpContext.TraceIdentifier)),
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            CurrentIdentity? identity = identityAccessor.GetCurrentIdentity(httpContext.User);
            Guid? actorId = identity is not null && Guid.TryParse(identity.Subject, out Guid parsedSub)
                ? parsedSub
                : null;

            int horizon = monthsAhead ?? 3;
            if (horizon < 0 || horizon > 24)
            {
                return Results.Json(
                    new ApiErrorResponse(
                        new ApiErrorDetail("VALIDATION_ERROR", "monthsAhead must be between 0 and 24."),
                        new ApiMeta(httpContext.TraceIdentifier)),
                    statusCode: StatusCodes.Status400BadRequest);
            }

            await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
            IReadOnlyList<string> ensured = await AuditPartitionManager.EnsureMonthlyPartitionsAsync(
                conn,
                DateTimeOffset.UtcNow,
                horizon,
                cancellationToken);

            await using NpgsqlTransaction tx = await conn.BeginTransactionAsync(cancellationToken);
            await AuditWriter.WriteAsync(
                conn,
                tx,
                new AuditRecord(
                    Domain: "audit",
                    EventType: "audit.partitions_ensured",
                    EntityType: "audit_partition",
                    EntityId: actorId ?? Guid.Empty,
                    ActorId: actorId,
                    PayloadJson: $"{{\"count\":{ensured.Count}}}"),
                cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return Results.Ok(new ApiResponse<EnsurePartitionsResult>(
                new EnsurePartitionsResult(ensured),
                new ApiMeta(httpContext.TraceIdentifier)));
        })
        .RequireAdminPermission("audit", "manage")
        .RequireIdempotency();
    }
}
