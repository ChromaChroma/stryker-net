using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using Stryker.Abstractions;
using Stryker.Abstractions.Testing;
using Stryker.TestRunner.Results;
using Stryker.Utilities.Logging;
using static Stryker.Abstractions.Testing.ITestRunner;

namespace Stryker.Core.MutationTest;

/// <summary>
/// Executes exactly one mutation test and stores the result
/// </summary>
public interface IMutationTestExecutor
{
    ITestRunner TestRunner { get; }

    void Test(IProjectAndTests project, IList<IMutant> mutantsToTest, ITimeoutValueCalculator timeoutMs,
        TestUpdateHandler updateHandler);
}

public class MutationTestExecutor : IMutationTestExecutor
{
    public ITestRunner TestRunner { get; }
    private ILogger Logger { get; }

    public MutationTestExecutor(ITestRunner testRunner)
    {
        TestRunner = testRunner;
        Logger = ApplicationLogging.LoggerFactory.CreateLogger<MutationTestProcess>();
    }

    public void Test(IProjectAndTests project, IList<IMutant> mutantsToTest, ITimeoutValueCalculator timeoutMs,
        TestUpdateHandler updateHandler)
    {
        var forceSingle = false;

        var stopwatch = new Stopwatch();
        stopwatch.Start();
        var timeoutCounter = 0;
        var mutants = $"{mutantsToTest.Count}:{string.Join(" ,", mutantsToTest.Select(x => x.Mutation.ReplacementNode.ToString()))}";

        while (mutantsToTest.Any())
        {
            var result = RunTestSession(project, mutantsToTest, timeoutMs, updateHandler, forceSingle);

            if (result.SessionTimedOut || result.TimedOutTests.Count > 0)
            {
                timeoutCounter++;
            }

            Logger.LogDebug("===Mutant Testing ({Status})({Nr}) ({ElapsedMilliseconds} ms) of: {Mutants}",
                result.SessionTimedOut || result.TimedOutTests.Count > 0 ? $"{timeoutCounter} timeouts" : "no timeout",
                result.TimedOutTests.Count,
                stopwatch.ElapsedMilliseconds,
                $"{mutantsToTest.Count}:{string.Join(" ,", mutantsToTest.Select(x => x.Mutation.DisplayName.ToString() +"__" + x.Mutation.ReplacementNode))}");

            Logger.LogDebug(
                "Test run for {Mutants} is {Result} ",
                string.Join(", ", mutantsToTest.Select(x => x.DisplayName)),
                result.FailingTests.Count == 0 ? "success" : "failed");

            if (result.Messages is not null && result.Messages.Any())
            {
                Logger.LogTrace(
                    "Messages for {Mutants}: {NewLine}{Messages}",
                    string.Join(", ", mutantsToTest.Select(x => x.DisplayName)),
                    Environment.NewLine,
                    string.Join("", result.Messages));
            }

            var remainingMutants = mutantsToTest.Where((m) => m.ResultStatus == MutantStatus.Pending).ToList();
            if (remainingMutants.Count == mutantsToTest.Count)
            {
                // the test failed to get any conclusive results
                if (!result.SessionTimedOut)
                {
                    // something bad happened.
                    Logger.LogError("Stryker failed to test {RemainingMutantsCount} mutant(s).", remainingMutants.Count);
                    return;
                }

                // test session's results have been corrupted by the time out
                // we retry and run tests one by one, if necessary
                if (remainingMutants.Count == 1)
                {
                    // only one mutant was tested, we mark it as timeout.
                    remainingMutants[0].ResultStatus = MutantStatus.Timeout;
                    remainingMutants.Clear();
                }
                else
                {
                    // we don't know which tests timed out, we rerun all tests in dedicated sessions
                    forceSingle = true;
                }
            }

            if (remainingMutants.Any())
            {
                Logger.LogDebug("Not all mutants were tested.");
            }

            mutantsToTest = remainingMutants;
        }

        stopwatch.Stop();
        Logger.LogDebug("===Mutant Testing (END) ({ElapsedMilliseconds} ms) of: {Mutants}", stopwatch.ElapsedMilliseconds, mutants);
    }

    private ITestRunResult RunTestSession(IProjectAndTests projectAndTests, ICollection<IMutant> mutantsToTest,
        ITimeoutValueCalculator timeoutMs,
        TestUpdateHandler updateHandler, bool forceSingle)
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        var checkpoint = stopwatch.ElapsedMilliseconds;

        Logger.LogTrace("Testing {MutantsToTest}.", string.Join(" ,", mutantsToTest.Select(x => x.DisplayName)));
        if (forceSingle)
        {
            foreach (var mutant in mutantsToTest)
            {
                var localResult =
                    TestRunner.TestMultipleMutants(projectAndTests, timeoutMs, new[] { mutant }, updateHandler);
                if (updateHandler == null || localResult.SessionTimedOut)
                {
                    mutant.AnalyzeTestRun(localResult.FailingTests,
                        localResult.ExecutedTests,
                        localResult.TimedOutTests,
                        localResult.SessionTimedOut);
                }
                Logger.LogDebug("===Mutant Testing forceSingle ({ElapsedMilliseconds} ms) of: {Mutants}",
                    stopwatch.ElapsedMilliseconds - checkpoint,
                    $"{mutant.Id}:{mutant.Mutation.DisplayName}__{mutant.Mutation.ReplacementNode}");

            }

            return new TestRunResult(true);
        }

        var result = TestRunner.TestMultipleMutants(projectAndTests, timeoutMs, mutantsToTest.ToList(), updateHandler);
        if (updateHandler != null && !result.SessionTimedOut)
        {
            return result;
        }

        foreach (var mutant in mutantsToTest)
        {
            mutant.AnalyzeTestRun(result.FailingTests,
                result.ExecutedTests,
                result.TimedOutTests,
                mutantsToTest.Count == 1 && result.SessionTimedOut);
        }

        return result;
    }
}
