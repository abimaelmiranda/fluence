namespace Fluence.Core.Debug;

public interface IDebugStateService
{
    event EventHandler? Changed;

    DebugSessionSnapshot Snapshot { get; }

    IReadOnlyList<DebugBreakpoint> ToggleBreakpoint(string filePath, int line);

    void SetBreakpointVerification(string filePath, IReadOnlyDictionary<int, DebugBreakpoint> breakpointsByLine);

    void StartSession();

    void SetStopped(string? reason, int threadId, DebugExecutionLine? currentLine);

    void SetInspectionData(
        IReadOnlyList<DebugStackFrame> stackFrames,
        IReadOnlyList<DebugVariable> variables);

    void Continue();

    void EndSession();
}
