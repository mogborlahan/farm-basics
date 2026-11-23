using System.Globalization;
using FarmBasics.Api.Auth;
using FarmBasics.Api.Persistence;
using FarmBasics.Api.Registration;
using FarmBasics.BuildingBlocks.Api;
using FarmBasics.BuildingBlocks.Observability;
using Npgsql;

namespace FarmBasics.Api.Observability;

internal sealed record InvariantCanaryReport(
    DateTimeOffset CheckedAtUtc,
    bool CanaryRunSucceeded,
    int UnbalancedJournalEntries,
    int MissingMonthlyPartitions,
    int SelfApprovedMasterDataRequests,
    int UnresolvedReconciliationAlerts,
    bool AllInvariantsHealthy);

/// <summary>
/// Live CRITICAL invariant-violation canary sweep (WP-A0 / S10.1.M2 / Doc 08-ENG §5.1).
/// Queries structurally-unreachable invariants directly in PostgreSQL and publishes Prometheus gauges.
/// </summary>
internal static partial class InvariantCanaryService
{
    public static void MapObservabilityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/admin/observability/canaries", HandleGetCanariesAsync)
            .RequireAuthorization()
            .RequireAdminPermission("audit", "search");
    }

    public static async Task<InvariantCanaryReport> RunSweepAsync(
        NpgsqlDataSource dataSource,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(logger);

        DateTime utcNow = DateTime.UtcNow;
        DateTime currentMonth = new(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime nextMonth = currentMonth.AddMonths(1);

        string auditCurr = $"audit.audit_log_entry_{currentMonth.ToString("yyyy_MM", CultureInfo.InvariantCulture)}";
        string auditNext = $"audit.audit_log_entry_{nextMonth.ToString("yyyy_MM", CultureInfo.InvariantCulture)}";
        string journalCurr = $"accounts.journal_entry_{currentMonth.ToString("yyyy_MM", CultureInfo.InvariantCulture)}";
        string journalNext = $"accounts.journal_entry_{nextMonth.ToString("yyyy_MM", CultureInfo.InvariantCulture)}";

        try
        {
            await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(cancellationToken);
            await using NpgsqlCommand cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT
                    (
                        SELECT COUNT(*)::int
                        FROM (
                            SELECT journal_entry_id, journal_entry_date
                            FROM accounts.journal_entry_line
                            GROUP BY journal_entry_id, journal_entry_date
                            HAVING COALESCE(SUM(debit_amount), 0) <> COALESCE(SUM(credit_amount), 0)
                        ) AS unbalanced
                    ) AS unbalanced_journal_entries,
                    (
                        (to_regclass(@auditCurr) IS NULL)::int +
                        (to_regclass(@auditNext) IS NULL)::int +
                        (to_regclass(@journalCurr) IS NULL)::int +
                        (to_regclass(@journalNext) IS NULL)::int
                    ) AS missing_partitions,
                    (
                        SELECT COUNT(*)::int
                        FROM admin.master_data_change_request
                        WHERE reviewed_by_admin_id IS NOT NULL
                          AND reviewed_by_admin_id = proposed_by_admin_id
                    ) AS self_approved_master_data,
                    (
                        SELECT COUNT(*)::int
                        FROM accounts.reconciliation_alert
                        WHERE resolved = FALSE
                    ) AS open_reconciliation_alerts;
                """;

            cmd.Parameters.AddWithValue("auditCurr", auditCurr);
            cmd.Parameters.AddWithValue("auditNext", auditNext);
            cmd.Parameters.AddWithValue("journalCurr", journalCurr);
            cmd.Parameters.AddWithValue("journalNext", journalNext);

            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);

            int unbalanced = reader.GetInt32(0);
            int missingPartitions = reader.GetInt32(1);
            int selfApproved = reader.GetInt32(2);
            int openReconciliationAlerts = reader.GetInt32(3);

            PlatformMetrics.InvariantUnbalancedJournalEntries.Set(unbalanced);
            PlatformMetrics.InvariantMissingMonthlyPartitions.Set(missingPartitions);
            PlatformMetrics.InvariantSelfApprovedMasterData.Set(selfApproved);
            PlatformMetrics.InvariantReconciliationOpenAlerts.Set(openReconciliationAlerts);
            PlatformMetrics.InvariantCanaryLastRunSuccess.Set(1);

            bool allHealthy = unbalanced == 0 && missingPartitions == 0 && selfApproved == 0 && openReconciliationAlerts == 0;
            if (!allHealthy)
            {
                LogCriticalInvariantBreach(logger, unbalanced, missingPartitions, selfApproved, openReconciliationAlerts);
            }

            return new InvariantCanaryReport(
                CheckedAtUtc: DateTimeOffset.UtcNow,
                CanaryRunSucceeded: true,
                UnbalancedJournalEntries: unbalanced,
                MissingMonthlyPartitions: missingPartitions,
                SelfApprovedMasterDataRequests: selfApproved,
                UnresolvedReconciliationAlerts: openReconciliationAlerts,
                AllInvariantsHealthy: allHealthy);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            PlatformMetrics.InvariantCanaryLastRunSuccess.Set(0);
            LogCanarySweepFailed(logger, ex.GetType().Name);
            throw;
        }
    }

    private static async Task<IResult> HandleGetCanariesAsync(
        HttpContext httpContext,
        DatabaseConnectionSource dbSource,
        SchemaMigrationRunner migrationRunner,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ILogger logger = loggerFactory.CreateLogger("FarmBasics.Api.Observability.InvariantCanaryService");
        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            PlatformMetrics.InvariantCanaryLastRunSuccess.Set(0);
            LogCanarySweepFailed(logger, "MissingDataSource");
            return FailClosedCanaryResponse(httpContext);
        }

        try
        {
            await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);
            InvariantCanaryReport report = await RunSweepAsync(dataSource, logger, cancellationToken);
            return Results.Ok(new ApiResponse<InvariantCanaryReport>(report, new ApiMeta(httpContext.TraceIdentifier)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            PlatformMetrics.InvariantCanaryLastRunSuccess.Set(0);
            LogCanarySweepFailed(logger, ex.GetType().Name);
            return FailClosedCanaryResponse(httpContext);
        }
    }

    private static IResult FailClosedCanaryResponse(HttpContext httpContext) => Results.Json(
        new ApiResponse<InvariantCanaryReport>(
            new InvariantCanaryReport(
                CheckedAtUtc: DateTimeOffset.UtcNow,
                CanaryRunSucceeded: false,
                UnbalancedJournalEntries: -1,
                MissingMonthlyPartitions: -1,
                SelfApprovedMasterDataRequests: -1,
                UnresolvedReconciliationAlerts: -1,
                AllInvariantsHealthy: false),
            new ApiMeta(httpContext.TraceIdentifier)),
        statusCode: StatusCodes.Status503ServiceUnavailable);

    [LoggerMessage(
        Level = LogLevel.Critical,
        Message = "CRITICAL invariant breach detected: UnbalancedJournalEntries={UnbalancedJournalEntries}, MissingMonthlyPartitions={MissingMonthlyPartitions}, SelfApprovedMasterData={SelfApprovedMasterData}, OpenReconciliationAlerts={OpenReconciliationAlerts}")]
    private static partial void LogCriticalInvariantBreach(
        ILogger logger,
        int unbalancedJournalEntries,
        int missingMonthlyPartitions,
        int selfApprovedMasterData,
        int openReconciliationAlerts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invariant canary background sweep failed with {ErrorType}")]
    internal static partial void LogCanarySweepFailed(ILogger logger, string errorType);
}

internal sealed class InvariantCanaryBackgroundService(
    DatabaseConnectionSource dbSource,
    SchemaMigrationRunner migrationRunner,
    ILogger<InvariantCanaryBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(5));
        await RunOnceSafelyAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceSafelyAsync(stoppingToken);
        }
    }

    private async Task RunOnceSafelyAsync(CancellationToken cancellationToken)
    {
        NpgsqlDataSource? dataSource = dbSource.DataSource;
        if (dataSource is null)
        {
            return;
        }

        try
        {
            await migrationRunner.EnsureMigratedAsync(dataSource, cancellationToken);
            await InvariantCanaryService.RunSweepAsync(dataSource, logger, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            PlatformMetrics.InvariantCanaryLastRunSuccess.Set(0);
            InvariantCanaryService.LogCanarySweepFailed(logger, ex.GetType().Name);
        }
    }
}
