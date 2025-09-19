using System;
using System.Collections.Generic;
using System.Linq;
using Stryker.Abstractions;
using Stryker.Abstractions.Memoization;
using Stryker.Abstractions.Testing;

namespace Stryker.TestRunner.Results;

public class CoverageRunResult : ICoverageRunResult
{
    public string TestId { get; }
    public CoverageConfidence Confidence { get; private set; }
    public Dictionary<int, MutationTestingRequirements> MutationFlags { get; } = new();
    public List<MetricData> MemoizationData { get; } = new();

    public IReadOnlyCollection<int> MutationsCovered => MutationFlags.Keys;

    public MutationTestingRequirements this[int mutation] => MutationFlags.GetValueOrDefault(mutation, MutationTestingRequirements.NotCovered);


    private CoverageRunResult(string testId, CoverageConfidence confidence, IEnumerable<int> coveredMutations,
        IEnumerable<int> detectedStaticMutations, IEnumerable<int> leakedMutations, List<MetricData> memoziationData)
    {
        TestId = testId;
        Confidence = confidence;
        MemoizationData = memoziationData;
        foreach (var coveredMutation in coveredMutations)
        {
            MutationFlags[coveredMutation] = MutationTestingRequirements.None;
        }

        foreach (var detectedStaticMutation in detectedStaticMutations)
        {
            MutationFlags[detectedStaticMutation] = MutationTestingRequirements.Static;
        }

        foreach (var leakedMutation in leakedMutations)
        {
            var requirement = confidence == CoverageConfidence.Exact
                ? MutationTestingRequirements.NeedEarlyActivation
                : MutationTestingRequirements.CoveredOutsideTest;

            MutationFlags[leakedMutation] = requirement;
        }


    }

    public static CoverageRunResult Create(
        string testId,
        CoverageConfidence confidence,
        IEnumerable<int> coveredMutations,
        IEnumerable<int> detectedStaticMutations,
        IEnumerable<int> leakedMutations,
        List<MetricData> memoizationData) =>
        new(testId, confidence, coveredMutations, detectedStaticMutations, leakedMutations, memoizationData);


    public void Merge(ICoverageRunResult coverageRunResult)
    {
        var coverage = (CoverageRunResult)coverageRunResult;
        Confidence = (CoverageConfidence)Math.Min((int)Confidence, (int)coverage.Confidence);
        foreach (var mutationFlag in coverage.MutationFlags)
        {
            if (MutationFlags.ContainsKey(mutationFlag.Key))
            {
                MutationFlags[mutationFlag.Key] |= mutationFlag.Value;
            }
            else
            {
                MutationFlags[mutationFlag.Key] = mutationFlag.Value;
            }
        }
        MemoizationData.AddRange(coverage.MemoizationData);
    }
}
