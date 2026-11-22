using System.Collections.Concurrent;

namespace FarmBasics.IntegrationTests.Infrastructure;

/// <summary>
/// Reusable genuine-parallel barrier-synchronised race harness mandated by
/// Document 12 §4.1, §9 and AGENTS.md Hard Rule 4.
/// Every participant blocks on a dedicated barrier until all participants are ready,
/// then fires simultaneously, repeated across N iterations (default 50).
/// </summary>
public sealed record RaceSummary(
    int Iterations,
    int Concurrency,
    int TotalWinners,
    int TotalConflicts,
    IReadOnlyList<int> WinnersPerIteration);

public static class ParallelRaceHarness
{
    public static async Task<RaceSummary> RunAsync(
        int iterations,
        int concurrency,
        Func<int, Task<Func<int, Task<bool>>>> iterationSetupAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 2);
        ArgumentNullException.ThrowIfNull(iterationSetupAsync);

        List<int> winnersPerIteration = new(iterations);
        int totalWinners = 0;
        int totalConflicts = 0;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            Func<int, Task<bool>> contenderAction = await iterationSetupAsync(iteration);
            using Barrier startBarrier = new(concurrency);
            ConcurrentBag<bool> outcomes = [];

            Task[] tasks = new Task[concurrency];
            for (int worker = 0; worker < concurrency; worker++)
            {
                int workerIndex = worker;
                tasks[worker] = Task.Factory.StartNew(
                    () => RunWorkerAsync(startBarrier, contenderAction, workerIndex, outcomes, cancellationToken),
                    cancellationToken,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).Unwrap();
            }

            await Task.WhenAll(tasks);

            int iterationWinners = outcomes.Count(won => won);
            int iterationConflicts = outcomes.Count - iterationWinners;

            winnersPerIteration.Add(iterationWinners);
            totalWinners += iterationWinners;
            totalConflicts += iterationConflicts;
        }

        return new RaceSummary(iterations, concurrency, totalWinners, totalConflicts, winnersPerIteration);
    }

    private static async Task RunWorkerAsync(
        Barrier startBarrier,
        Func<int, Task<bool>> contenderAction,
        int workerIndex,
        ConcurrentBag<bool> outcomes,
        CancellationToken cancellationToken)
    {
        startBarrier.SignalAndWait(cancellationToken);
        bool won = await contenderAction(workerIndex);
        outcomes.Add(won);
    }
}
