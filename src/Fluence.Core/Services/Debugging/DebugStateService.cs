using System;
using System.Collections.Generic;
using System.Linq;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;

namespace Fluence.Core.Services.Debugging;

public sealed class DebugStateService : IDebugStateService
{
    private readonly object _gate = new();
    private DebugSessionSnapshot _snapshot = new(
        IsActive: false,
        IsStopped: false,
        Status: DebugSessionStatus.Inactive,
        Architecture: "x64",
        Reason: null,
        ActiveThreadId: null,
        CurrentLine: null,
        ExceptionInfo: null,
        Breakpoints: [],
        StackFrames: [],
        Variables: []);

    public event EventHandler? Changed;

    public DebugSessionSnapshot Snapshot
    {
        get
        {
            lock (_gate)
                return _snapshot;
        }
    }

    public IReadOnlyList<DebugBreakpoint> ToggleBreakpoint(string filePath, int line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (line <= 0)
            return Snapshot.Breakpoints;
        lock (_gate)
        {
            var breakpoints = _snapshot.Breakpoints.ToList();
            var index = breakpoints.FindIndex(b =>
                string.Equals(b.FilePath, filePath, StringComparison.OrdinalIgnoreCase) &&
                b.Line == line);
            if (index >= 0)
            {
                breakpoints.RemoveAt(index);
            }
            else
            {
                breakpoints.Add(new DebugBreakpoint(filePath, line));
            }

            _snapshot = _snapshot with
            {
                Breakpoints = breakpoints
                    .OrderBy(b => b.FilePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(b => b.Line)
                    .ToArray(),
            };
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return Snapshot.Breakpoints;
    }

    public void SetBreakpointVerification(string filePath, IReadOnlyDictionary<int, DebugBreakpoint> breakpointsByLine)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                Breakpoints = _snapshot.Breakpoints
                    .Select(b => string.Equals(b.FilePath, filePath, StringComparison.OrdinalIgnoreCase) &&
                                 breakpointsByLine.TryGetValue(b.Line, out var updated)
                        ? b with
                        {
                            IsVerified = updated.IsVerified,
                            AdapterId = updated.AdapterId,
                            Message = updated.Message,
                        }
                        : b)
                    .ToArray(),
            };
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void StartSession(string architecture = "x64")
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                IsActive = true,
                IsStopped = false,
                Status = DebugSessionStatus.Starting,
                Architecture = string.IsNullOrWhiteSpace(architecture) ? "x64" : architecture,
                Reason = null,
                ActiveThreadId = null,
                CurrentLine = null,
                ExceptionInfo = null,
            };
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetStopped(
        string? reason,
        int threadId,
        DebugExecutionLine? currentLine,
        DebugExceptionInfo? exceptionInfo)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                IsActive = true,
                IsStopped = true,
                Status = DebugSessionStatus.Stopped,
                Reason = reason,
                ActiveThreadId = threadId,
                CurrentLine = currentLine,
                ExceptionInfo = exceptionInfo,
            };
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetInspectionData(
        IReadOnlyList<DebugStackFrame> stackFrames,
        IReadOnlyList<DebugVariable> variables)
    {
        lock (_gate)
            _snapshot = _snapshot with { StackFrames = stackFrames.ToArray(), Variables = variables.ToArray() };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Continue()
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                IsStopped = false,
                Status = DebugSessionStatus.Running,
                Reason = null,
                CurrentLine = null,
                ExceptionInfo = null,
            };
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void EndSession()
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                IsActive = false,
                IsStopped = false,
                Status = DebugSessionStatus.Inactive,
                Architecture = "x64",
                Reason = null,
                ActiveThreadId = null,
                CurrentLine = null,
                ExceptionInfo = null,
                StackFrames = [],
                Variables = [],
            };
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
