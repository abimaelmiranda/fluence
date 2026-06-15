using Fluence.Core.Models.Debugging.Enums;

namespace Fluence.Core.Models.Debugging;

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
