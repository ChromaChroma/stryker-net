using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Stryker.Abstractions;
using Stryker.Abstractions.Memoization;

namespace Stryker.Core.Memoization;

public class MetricDataCollection : IMetricDataCollection
{
    private static Guid testRunIdentifier = Guid.NewGuid();
    private readonly ConcurrentDictionary<string, int> _identifierMap = new();
    private readonly ConcurrentDictionary<string, int> _argsMap = new();
    private int _identifierCounter = 0;
    private int _argsCounter = 0;

    public ConcurrentBag<MetricData> RawEntries { get; private set; } = new();

    private ConcurrentDictionary<string, ConcurrentBag<MetricData>> _orderedMetricData = new();

    public void Add(MetricData entry)
    {
        // Compress Identifier (thread-safe)
        entry.Identifier = _identifierMap.GetOrAdd(
            entry.Identifier,
            _ => Interlocked.Increment(ref _identifierCounter)
        ).ToString();


        // Compress StringifiedArguments (thread-safe)
        entry.StringifiedArguments = _argsMap.GetOrAdd(
            entry.StringifiedArguments,
            _ => Interlocked.Increment(ref _argsCounter)
        ).ToString();


        var metricDatas = _orderedMetricData.GetOrAdd(entry.Identifier, _ => new ConcurrentBag<MetricData>());
        metricDatas.Add(entry);
        RawEntries.Add(entry);
    }

    public void AddAll(List<MetricData> entries)
    {
        foreach (var entry in entries)
        {
            Add(entry);
        }
    }

    private static long Percentile(long[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0) return 0;

        double rank = (percentile / 100.0) * (sortedValues.Length - 1);
        int lower = (int)Math.Floor(rank);
        int upper = (int)Math.Ceiling(rank);

        if (lower == upper)
        {
            return sortedValues[lower];
        }

        double weight = rank - lower;
        return (long)Math.Round(sortedValues[lower] * (1 - weight) + (sortedValues[upper] * weight));
    }

    // private FieldTimingStats CreateTimingStats(long[] values)
    // {
    //     Array.Sort(values);
    //     int n = values.Length;
    //     long min = values[0];
    //     long max = values[^1];
    //     double avg = values.Average();
    //     double median = (n % 2 == 0)
    //         ? (values[n / 2 - 1] + values[n / 2]) / 2.0
    //         : values[n / 2];
    //     double variance = values.Select(v => Math.Pow(v - avg, 2)).Sum() / n;
    //     double stdDev = Math.Sqrt(variance);
    //     long p95 = Percentile(values, 95);
    //     long p99 = Percentile(values, 99);
    //
    //     return new FieldTimingStats
    //     {
    //         Min = min,
    //         Max = max,
    //         Avg = avg,
    //         Median = median,
    //         StdDev = stdDev,
    //         P95 = p95,
    //         P99 = p99
    //     };
    // }

    public MetricResultReport CreateMetricResultReport()
    {
        long totalHits = RawEntries.Count(e => e.IsMemoHit);

        return new MetricResultReport
        {
            TestRunId = testRunIdentifier,
            MutationScore = MemoizationTimingCollector.MutationScore,
            MemoizationProcessTimings = MemoizationTimingCollector.Timings.ToList(),
            NotMemoizedReasons = NotMemoizedCollector.Reasons.ToList(),
            TotalMemoizationInjectionCalls = RawEntries.Count,
            TotalUniqueMemoizationInjectionCalls = RawEntries.ToList()
                .Select(m => m.Identifier)
                .Distinct()
                .Count(),
            TotalHits = totalHits,
            TotalMisses = RawEntries.Count - totalHits,
            HitMissRatio = RawEntries.Count != 0 ? (double)totalHits / RawEntries.Count : 0,
            RawEntries = RawEntries.ToList(),
            PerMemoizationStatistics = []
            // _orderedMetricData.Values.Where(list => list.Count != 0)
            //     .Select(metricData =>
            //     {
            //         var firstEntry = metricData.First();
            //         var totalhits = 0;
            //         long[] checkSerializabilityTimings = new long[metricData.Count],
            //             serilizationTimings = new long[metricData.Count],
            //             deserilizationTimings = new long[metricData.Count],
            //             retrievingTimings = new long[metricData.Count],
            //             storingTimings = new long[metricData.Count],
            //             totalMemoziationTimings = new long[metricData.Count];
            //         var metricDataArray = metricData.ToArray();
            //         for (var i = 0; i < metricDataArray.Length; i++)
            //         {
            //             var data = metricDataArray[i];
            //             if (data.IsMemoHit)
            //             {
            //                 totalhits++;
            //             }
            //
            //             checkSerializabilityTimings[i] = data.TimeToCheckSerializibility;
            //             serilizationTimings[i] = data.TimeToSerialize;
            //             deserilizationTimings[i] = data.TimeToDeserialize;
            //             retrievingTimings[i] = data.TimeToTryGetValue;
            //             storingTimings[i] = data.TimeToStore;
            //             totalMemoziationTimings[i] = data.TimeTotal;
            //         }
            //
            //         return new MemoizationInjectionStatistics
            //         {
            //             MemoizationIdentiier = firstEntry.Identifier,
            //             TotalTimesCalled = metricData.Count,
            //             TotalHits = totalhits,
            //             TotalMisses = metricData.Count - totalhits,
            //             HitMissRatio = totalhits / metricData.Count,
            //             CheckSerializabilityStatistics = CreateTimingStats(checkSerializabilityTimings),
            //             SerializationStatistics = CreateTimingStats(serilizationTimings),
            //             DeserializationStatistics = CreateTimingStats(deserilizationTimings),
            //             RetrievingMemoizationStatistics = CreateTimingStats(retrievingTimings),
            //             StoringMemoizationStatistics = CreateTimingStats(storingTimings),
            //             TotalMemoizationStatistics = CreateTimingStats(totalMemoziationTimings)
            //         };
            //     })
            //     .ToList()
        };
    }
}
