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

    public static string ClassifyStatusCode(int statusCode) => statusCode switch
    {
        409 => "409_conflict",
        >= 200 and < 300 => "2xx",
        >= 300 and < 400 => "3xx",
        >= 400 and < 500 => "4xx",
        _ => "5xx"
    };
}
