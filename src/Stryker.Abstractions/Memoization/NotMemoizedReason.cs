using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;

namespace Stryker.Abstractions.Memoization;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ReasonType
{
    None,
    Other,
    ParameterTypeNotSerializable,
    ReturnTypeNotSerializable,
    VoidReturnType,
    // IllegalModifiers, // e.g. ref, out, in, params, yield, etc
    IllegalModifiersYield, // e.g. ref, out, in, params, yield, etc
    IllegalModifiersRef, // e.g. ref, out, in, params, yield, etc
    IllegalModifiersOut, // e.g. ref, out, in, params, yield, etc
    IllegalModifiersIn, // e.g. ref, out, in, params, yield, etc
    PointerTypes, // unsafe, IntPtr, UIntPtr
    Dynamic, //  Dynamic / ExpandoObject
    NonDeterministic, // DateTime.Now, Random, Guid.NewGuid, etc
    DateOrTimingSensitive, // DateTime, TimeSpan, Stopwatch, etc
    UnverifiableEnvironmentalDependency,
    IOBound, // File, Network, Database, etc Reads files, sockets, streams → memoizing would freeze the value.

    //Side effects
    // NonInterferingSideEffects, // Logging, Console, etc
    ReliesOnVolitileExternalState, // Relies on external state that may change between invocations, such as environment variables, configuration settings, or system properties
    ReliesOnExternalState, // Relies on external state that may change between invocations, such as static variables, singletons, or external services
    ReliesOnThisState, // Relies on instance state (this) that may change between invocations
    AltersStateOutsideScope, // Modifies global/static state, instance state, or parameters passed by reference that is not local to the scope of the memoized code
    AllocatesUnstableOrDisposableResources, // Allocates resources that are unstable or disposable, such as file handles, network connections, or database connections
    InvokesUnstableOrDisposableResources, // Invokes methods that are unstable or disposable,

    ThrowsExceptions, // Throws exceptions that are not handled within the memoized code
    ReliesOnGarbageCollection, // Relies on garbage collection to manage memory or resources
    UsesThreadingOrAsynchronousOperations, // Uses threading or asynchronous operations that may lead to

    LocksOrUnlocksResources, // Locks or unlocks resources that may lead to dead
    UsesReflection, // Uses reflection to access or modify code at runtime
    UsesUnsafeCode, // Uses unsafe code that may lead to memory corruption or security vulnerabilities
    UsesExternalLibrariesOrAPIs, // Uses external libraries or APIs that may not be reliable
    UsesEventsOrCallbacks, // Uses events or callbacks that may lead to unpredictable behavior
    FinalizersOrDestructors, // Uses finalizers or destructors that may lead to unpredictable behavior
    DynamicILGeneration,
    NoImplementation,
    SwitchExpressionInRightHandSide,
    ParentIsValueType,
    ExtensionMethod,
    ContainsLinqQuery
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MemoizationLevel
{
    Method,
    Scope,
    Expression,
}

public struct NotMemoizedReason
{
    public NotMemoizedReason()
    {
    }

    public ReasonType Type { get; set; } = ReasonType.None;
    public MemoizationLevel MemoizationLevel { get; set; }
    public string Reason { get; set; } = "";
    public string? NodeCodeString { get; set; } = "";

    public NotMemoizedReason(ReasonType type, MemoizationLevel level, string reason, string? node)
    {
        Type = type;
        MemoizationLevel = level;
        Reason = reason;
        NodeCodeString = node;
    }
}

public static class NotMemoizedCollector
{
    public static readonly ConcurrentBag<NotMemoizedReason> Reasons = new();

    public static void Add(ReasonType type, MemoizationLevel level, string  reason, SyntaxNode node) => Reasons.Add(new NotMemoizedReason(type, level, reason, node?.ToString() ?? ""));
}
