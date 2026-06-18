namespace Fluence.Core.Models.Debugging;

public sealed record DebugAdapterStoppedEvent(
    string? Reason,
    int ThreadId,
    string? Description,
    string? Text);

public sealed record DebugAdapterTerminatedEvent;

public sealed record DebugAdapterContinuedEvent;

public sealed record DebugAdapterOutputEvent(
    string Text,
    bool IsError);
