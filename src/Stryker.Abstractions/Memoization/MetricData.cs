namespace Stryker.Abstractions.Memoization;

public struct MetricData(
    string identifier,
    string stringifiedArguments,
    bool isMemoHit,
    long timeTotal,
    long timeToCheckSerializibility,
    long timeToTryGetValue,
    long timeToDeserialize,
    long timeToSerialize,
    long timeToStore,
    string memoValueNotEqualToComputedMessage)
{
    public string Identifier { get; set;  } = identifier;
    public string StringifiedArguments { get; set;  } = stringifiedArguments;
    public bool IsMemoHit { get; } = isMemoHit;
    public long TimeTotal { get; } = timeTotal;
    public long TimeToCheckSerializibility { get; } = timeToCheckSerializibility;
    public long TimeToTryGetValue { get; } = timeToTryGetValue;
    public long TimeToDeserialize { get; } = timeToDeserialize;
    public long TimeToSerialize { get; } = timeToSerialize;
    public long TimeToStore { get; } = timeToStore;
    public string MemoizedValueNotEqualToComputedMessage { get; } = memoValueNotEqualToComputedMessage;
    public override string ToString() => $"{Identifier},{StringifiedArguments},{IsMemoHit},{TimeTotal},{TimeToCheckSerializibility},{TimeToTryGetValue},{TimeToDeserialize},{TimeToSerialize},{TimeToStore} ";
}
