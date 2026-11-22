using FarmBasics.IntegrationTests.Infrastructure;
using Xunit;

namespace FarmBasics.UnitTests.Concurrency;

public sealed class ParallelRaceHarnessUnitTests
{
    [Fact]
    public async Task BarrierSynchronisedSingleWinnerLatchPermitsExactlyOneWinnerAcross50Iterations()
    {
        RaceSummary summary = await ParallelRaceHarness.RunAsync(
            iterations: 50,
            concurrency: 8,
            iterationSetupAsync: _ =>
            {
                int claimFlag = 0;
                return Task.FromResult(async (int _) =>
                {
                    await Task.Yield();
                    return Interlocked.CompareExchange(ref claimFlag, 1, 0) == 0;
                });
            });

        Assert.Equal(50, summary.Iterations);
        Assert.Equal(50, summary.TotalWinners);
        Assert.Equal(350, summary.TotalConflicts);
        Assert.All(summary.WinnersPerIteration, winners => Assert.Equal(1, winners));
    }
}
