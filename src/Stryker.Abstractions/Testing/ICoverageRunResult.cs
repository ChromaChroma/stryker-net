using System.Collections.Generic;
using Stryker.Abstractions.Memoization;

namespace Stryker.Abstractions.Testing;

public interface ICoverageRunResult
{
    string TestId { get; }
    CoverageConfidence Confidence { get; }
    Dictionary<int, MutationTestingRequirements> MutationFlags { get; }
    public List<MetricData> MemoizationData { get; }
    IReadOnlyCollection<int> MutationsCovered { get; }
    MutationTestingRequirements this[int mutation] { get; }
    void Merge(ICoverageRunResult coverageRunResult);
}
