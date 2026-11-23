using Prometheus;

namespace FarmBasics.BuildingBlocks.Observability;

/// <summary>
/// Real Prometheus golden-signal metrics exposed at /metrics for internal Prometheus scraping (WP-05 / 08-ENG §4).
/// Distinguishes 409 contention responses from generic 4xx and 5xx failures.
/// </summary>
public static class PlatformMetrics
{
    public static readonly Counter HttpRequestsTotal = Metrics.CreateCounter(
        "farmbasics_http_requests_total",
        "Total HTTP requests processed by the Farm Basics API.",
        new CounterConfiguration
        {
            LabelNames = ["method", "route", "status_class"]
        });

    public static readonly Histogram HttpRequestDurationSeconds = Metrics.CreateHistogram(
        "farmbasics_http_request_duration_seconds",
        "HTTP request duration in seconds.",
        new HistogramConfiguration
        {
            LabelNames = ["method", "route"],
            Buckets = Histogram.ExponentialBuckets(0.005, 2, 12)
        });

    public static readonly Counter LedgerReconciliationDriftTotal = Metrics.CreateCounter(
        "farmbasics_ledger_reconciliation_drift_total",
        "CRITICAL count of AccountBalance cache divergences detected against journal_entry_line.");

    public static readonly Gauge InvariantUnbalancedJournalEntries = Metrics.CreateGauge(
        "farmbasics_invariant_unbalanced_journal_entries",
        "CRITICAL canary count of unbalanced journal entries (must always be 0).");

    public static readonly Gauge InvariantMissingMonthlyPartitions = Metrics.CreateGauge(
        "farmbasics_invariant_missing_monthly_partitions",
        "CRITICAL canary count of missing current/next monthly partitions across audit and ledger tables (must always be 0).");

    public static readonly Gauge InvariantSelfApprovedMasterData = Metrics.CreateGauge(
        "farmbasics_invariant_self_approved_master_data",
        "CRITICAL canary count of self-approved master data change requests (must always be 0).");

    public static readonly Gauge InvariantReconciliationOpenAlerts = Metrics.CreateGauge(
        "farmbasics_invariant_reconciliation_open_alerts",
        "CRITICAL canary count of unresolved ledger reconciliation drift alerts (must always be 0).");

    public static readonly Gauge InvariantCanaryLastRunSuccess = Metrics.CreateGauge(
        "farmbasics_invariant_canary_last_run_success",
        "1 when the latest CRITICAL invariant canary sweep succeeded; 0 otherwise.");

    public static readonly Counter PilotBoundaryWalkOutcomesTotal = Metrics.CreateCounter(
        "farmbasics_pilot_boundary_walk_outcomes_total",
        "Pilot telemetry count of boundary walk outcomes without raw coordinates or PII (Doc 13 §6-7).",
        new CounterConfiguration
        {
            LabelNames = ["outcome"]
        });

    public static readonly Counter PilotOperationLogsTotal = Metrics.CreateCounter(
        "farmbasics_pilot_operation_logs_total",
        "Pilot telemetry count of crop operation task logs by validation status (Doc 13 §6-7).",
        new CounterConfiguration
        {
            LabelNames = ["status"]
        });

    public static readonly Counter PilotSyncOutcomesTotal = Metrics.CreateCounter(
        "farmbasics_pilot_sync_outcomes_total",
        "Pilot telemetry count of offline sync outcomes (Doc 13 §6-7).",
        new CounterConfiguration
        {
            LabelNames = ["outcome"]
        });

    public static string ClassifyStatusCode(int statusCode) => statusCode switch
    {
        409 => "409_conflict",
        >= 200 and < 300 => "2xx",
        >= 300 and < 400 => "3xx",
        >= 400 and < 500 => "4xx",
        _ => "5xx"
    };
}
