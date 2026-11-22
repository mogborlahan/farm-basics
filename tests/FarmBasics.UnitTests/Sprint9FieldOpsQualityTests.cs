using FarmBasics.Api.FieldOps;
using Xunit;

namespace FarmBasics.UnitTests;

public sealed class Sprint9FieldOpsQualityTests
{
    [Fact]
    public void SmallSampleSuppressesFlagAndRejectionRates()
    {
        FieldOpsQualitySignal signal = FieldOpsQualityCalculator.Compute(
            totalLogs: 9,
            flaggedLogs: 5,
            totalReviews: 4,
            rejectedReviews: 4);

        Assert.False(signal.IsLogSampleSufficient);
        Assert.Null(signal.FlagRate);
        Assert.False(signal.IsReviewSampleSufficient);
        Assert.Null(signal.RejectionRate);
        Assert.Equal("insufficient_sample", signal.SignalStatus);
    }

    [Fact]
    public void SufficientSampleComputesExactRatesAndElevatedSignal()
    {
        FieldOpsQualitySignal elevated = FieldOpsQualityCalculator.Compute(
            totalLogs: 10,
            flaggedLogs: 3,
            totalReviews: 12,
            rejectedReviews: 1);

        Assert.True(elevated.IsLogSampleSufficient);
        Assert.Equal(0.3000m, elevated.FlagRate);
        Assert.True(elevated.IsReviewSampleSufficient);
        Assert.Equal(0.0833m, elevated.RejectionRate);
        Assert.Equal("elevated", elevated.SignalStatus);
    }

    [Fact]
    public void SufficientSampleBelowThresholdRemainsNormalAndClampsBadCounts()
    {
        FieldOpsQualitySignal normal = FieldOpsQualityCalculator.Compute(
            totalLogs: 20,
            flaggedLogs: 2,
            totalReviews: -5,
            rejectedReviews: 99);

        Assert.True(normal.IsLogSampleSufficient);
        Assert.Equal(0.1000m, normal.FlagRate);
        Assert.False(normal.IsReviewSampleSufficient);
        Assert.Null(normal.RejectionRate);
        Assert.Equal(0, normal.TotalReviews);
        Assert.Equal(0, normal.RejectedReviews);
        Assert.Equal("normal", normal.SignalStatus);
    }
}
