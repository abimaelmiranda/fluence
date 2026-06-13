namespace Fluence.Core.Debug;

public sealed record DebugSessionSnapshot(
    bool IsActive,
    bool IsStopped,
    DebugSessionStatus Status,
    string? Reason,
    int? ActiveThreadId,
    DebugExecutionLine? CurrentLine,
    IReadOnlyList<DebugBreakpoint> Breakpoints,
    IReadOnlyList<DebugStackFrame> StackFrames,
    IReadOnlyList<DebugVariable> Variables);
