using FarmBasics.Api.Auth;
using FarmBasics.Api.Persistence;
using FarmBasics.Api.Registration;
using FarmBasics.BuildingBlocks.Api;
using Npgsql;

namespace FarmBasics.Api.Observability;

/// <summary>
/// Strictly aggregated, zero-PII pilot cohort observation metrics (WP-B0 / S11.1.M2 / Doc 13 §6).
/// Never exposes farmer IDs, names, phones, NIN hashes, or parcel geometry.
/// </summary>
internal static class PilotTelemetryEndpoints
{
    public static void MapPilotTelemetryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/admin/observability/pilot-cohort", HandleGetPilotCohortAsync)
            .RequireAuthorization()
            .RequireAdminPermission("analytics", "read");
    }

    private static async Task<IResult> HandleGetPilotCohortAsync(
        HttpContext httpContext,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        CancellationToken cancellationToken)
    {
        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return SelfServiceRegistrationEndpoints.ErrorResult(httpContext, StatusCodes.Status503ServiceUnavailable, "SERVICE_UNAVAILABLE", "Database is not configured.");
        }

        await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);

        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                (SELECT COUNT(*)::int FROM field.field_tracking_session) AS boundary_sessions_total,
                (SELECT COUNT(*)::int FROM field.field_tracking_session WHERE resulting_production_unit_id IS NOT NULL) AS boundary_confirmed_count,
                (SELECT COUNT(*)::int FROM field.operation_log) AS total_operation_logs,
                (SELECT COUNT(*)::int FROM field.operation_log WHERE status = 'flag') AS flagged_operation_logs,
                (
                    SELECT COUNT(DISTINCT pu.farm_identity_id)::int
                    FROM field.operation_log ol
                    JOIN field.crop_cycle cc ON cc.id = ol.crop_cycle_id
                    JOIN field.production_unit pu ON pu.id = cc.production_unit_id
                    WHERE ol.server_received_at >= now() - interval '14 days'
                ) AS active_farmers_logging_14d,
                (SELECT COUNT(*)::int FROM platform.sync_spike_records) AS total_sync_records,
                (SELECT COUNT(*)::int FROM identity.registration_attestation) AS registration_attestation_count,
                (SELECT COUNT(*)::int FROM identity.registration_attestation WHERE teach_back_passed = TRUE) AS teach_back_passed_count;
            """;

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        int boundarySessionsTotal = reader.GetInt32(0);
        int boundaryConfirmedCount = reader.GetInt32(1);
        int totalOperationLogs = reader.GetInt32(2);
        int flaggedOperationLogs = reader.GetInt32(3);
        int activeFarmersLoggingLast14Days = reader.GetInt32(4);
        int totalSyncRecords = reader.GetInt32(5);
        int registrationAttestationCount = reader.GetInt32(6);
        int teachBackPassedCount = reader.GetInt32(7);

        decimal boundaryFirstAttemptSuccessRate = boundarySessionsTotal > 0
            ? Math.Round((decimal)boundaryConfirmedCount / boundarySessionsTotal, 4, MidpointRounding.AwayFromZero)
            : 0m;

        decimal teachBackPassRate = registrationAttestationCount > 0
            ? Math.Round((decimal)teachBackPassedCount / registrationAttestationCount, 4, MidpointRounding.AwayFromZero)
            : 0m;

        return Results.Ok(new ApiResponse<object>(
            new
            {
                containsRawPii = false,
                boundarySessionsTotal,
                boundaryConfirmedCount,
                boundaryFirstAttemptSuccessRate,
                totalOperationLogs,
                flaggedOperationLogs,
                activeFarmersLoggingLast14Days,
                totalSyncRecords,
                registrationAttestationCount,
                teachBackPassRate
            },
            new ApiMeta(httpContext.TraceIdentifier)));
    }
}
