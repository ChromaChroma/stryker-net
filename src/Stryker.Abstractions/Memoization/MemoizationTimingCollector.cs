using System.Collections.Concurrent;
using System.Diagnostics;

namespace Stryker.Abstractions.Memoization;

public struct MemoizationProcessTiming
{
    public MemoizationProcessTiming(double time, string location)
    {
        Time = time;
        Location = location;
    }

    public double Time { get; set; }
    public string Location { get; set; }
}
public class MemoizationTimingCollector
{
    public static double MutationScore { get; set; }
    public static readonly ConcurrentBag<MemoizationProcessTiming> Timings = new();
    public static void Add(double time, string location)
        => Timings.Add(new MemoizationProcessTiming(time, location));

}
