namespace FarmBasics.Api.FieldOps;

internal sealed record FieldOpsQualitySignal(
    int TotalLogs,
    int FlaggedLogs,
    bool IsLogSampleSufficient,
    decimal? FlagRate,
    int TotalReviews,
    int RejectedReviews,
    bool IsReviewSampleSufficient,
    decimal? RejectionRate,
    string SignalStatus);

/// <summary>
/// Small-sample-aware extension agent quality calculator (Doc/PRD-Volume-D FR-FO-001 &amp; Doc/03-ENG §13.3).
/// Suppresses noisy FlagRate / RejectionRate ratios until minimum observation volume is met.
/// </summary>
internal static class FieldOpsQualityCalculator
{
    public const int DefaultMinimumSampleSize = 10;
    public const decimal ElevatedRateThreshold = 0.25m;

    public static FieldOpsQualitySignal Compute(
        int totalLogs,
        int flaggedLogs,
        int totalReviews,
        int rejectedReviews,
        int minimumSampleSize = DefaultMinimumSampleSize)
    {
        int effectiveMin = Math.Max(1, minimumSampleSize);
        int safeTotalLogs = Math.Max(0, totalLogs);
        int safeFlaggedLogs = Math.Clamp(flaggedLogs, 0, safeTotalLogs);
        int safeTotalReviews = Math.Max(0, totalReviews);
        int safeRejectedReviews = Math.Clamp(rejectedReviews, 0, safeTotalReviews);

        bool logSufficient = safeTotalLogs >= effectiveMin;
        decimal? flagRate = logSufficient
            ? Math.Round((decimal)safeFlaggedLogs / safeTotalLogs, 4, MidpointRounding.AwayFromZero)
            : null;

        bool reviewSufficient = safeTotalReviews >= effectiveMin;
        decimal? rejectionRate = reviewSufficient
            ? Math.Round((decimal)safeRejectedReviews / safeTotalReviews, 4, MidpointRounding.AwayFromZero)
            : null;

        string signalStatus;
        if (!logSufficient && !reviewSufficient)
        {
            signalStatus = "insufficient_sample";
        }
        else if ((flagRate.HasValue && flagRate.Value >= ElevatedRateThreshold)
                 || (rejectionRate.HasValue && rejectionRate.Value >= ElevatedRateThreshold))
        {
            signalStatus = "elevated";
        }
        else
        {
            signalStatus = "normal";
        }

        return new FieldOpsQualitySignal(
            TotalLogs: safeTotalLogs,
            FlaggedLogs: safeFlaggedLogs,
            IsLogSampleSufficient: logSufficient,
            FlagRate: flagRate,
            TotalReviews: safeTotalReviews,
            RejectedReviews: safeRejectedReviews,
            IsReviewSampleSufficient: reviewSufficient,
            RejectionRate: rejectionRate,
            SignalStatus: signalStatus);
    }
}
