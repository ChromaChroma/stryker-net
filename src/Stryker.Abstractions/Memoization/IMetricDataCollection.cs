using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Stryker.Core.Memoization;

namespace Stryker.Abstractions.Memoization;

public interface IMetricDataCollection
{
    public ConcurrentBag<MetricData> RawEntries { get;  }
    public MetricResultReport CreateMetricResultReport();
}

public struct FieldTimingStats
{
    public long Min { get; init; }
    public long Max { get; init; }
    public double Avg { get; init; }
    public double Median { get; init; }
    public double StdDev { get; init; }
    public long P95 { get; init; }
    public long P99 { get; init; }
}

public struct MemoizationInjectionStatistics
{
    public string MemoizationIdentiier { get; init; }
    public long TotalTimesCalled { get; init; }
    public long TotalHits { get; init; }
    public long TotalMisses { get; init; }
    public double HitMissRatio { get; init; }

    public FieldTimingStats CheckSerializabilityStatistics { get; init; }
    public FieldTimingStats SerializationStatistics { get; init; }
    public FieldTimingStats DeserializationStatistics { get; init; }
    public FieldTimingStats RetrievingMemoizationStatistics { get; init; }
    public FieldTimingStats StoringMemoizationStatistics { get; init; }
    public FieldTimingStats TotalMemoizationStatistics { get; init; }
}

public record MetricResultReport
{
    public Guid TestRunId { get; init; }
    public long TotalMemoizationInjectionCalls { get; init; }
    public long TotalUniqueMemoizationInjectionCalls { get; init; }
    public long TotalHits { get; init; }
    public long TotalMisses { get; init; }
    public double HitMissRatio { get; init; }
    public List<MemoizationInjectionStatistics> PerMemoizationStatistics { get; init; }
    public List<MetricData> RawEntries  { get; init; }

    public List<NotMemoizedReason> NotMemoizedReasons { get; init; } = new List<NotMemoizedReason>();
}
