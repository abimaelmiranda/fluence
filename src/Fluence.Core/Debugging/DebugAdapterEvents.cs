namespace Fluence.Core.Debug;

public sealed record DebugAdapterStoppedEvent(
    string? Reason,
    int ThreadId);

public sealed record DebugAdapterTerminatedEvent;

public sealed record DebugAdapterContinuedEvent;

public sealed record DebugAdapterOutputEvent(
    string Text,
    bool IsError);
