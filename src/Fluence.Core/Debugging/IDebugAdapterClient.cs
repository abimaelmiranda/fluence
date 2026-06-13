namespace Fluence.Core.Debug;

public interface IDebugAdapterClient : IAsyncDisposable
{
    event EventHandler<DebugAdapterStoppedEvent>? Stopped;

    event EventHandler<DebugAdapterTerminatedEvent>? Terminated;

    event EventHandler<DebugAdapterContinuedEvent>? Continued;

    event EventHandler<DebugAdapterOutputEvent>? OutputReceived;

    Task StartAsync(DebugLaunchRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, IReadOnlyList<DebugBreakpoint>>> SetBreakpointsAsync(
        IReadOnlyList<DebugBreakpoint> breakpoints,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DebugBreakpoint>> SetBreakpointsForFileAsync(
        string filePath,
        IReadOnlyList<DebugBreakpoint> breakpoints,
        CancellationToken cancellationToken = default);

    Task CompleteConfigurationAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DebugStackFrame>> GetStackTraceAsync(int threadId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DebugVariable>> GetVariablesAsync(int frameId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DebugVariable>> GetChildVariablesAsync(int variablesReference, CancellationToken cancellationToken = default);

    Task<DebugVariable?> EvaluateAsync(string expression, int frameId, CancellationToken cancellationToken = default);

    Task ContinueAsync(CancellationToken cancellationToken = default);

    Task StepOverAsync(CancellationToken cancellationToken = default);

    Task StepIntoAsync(CancellationToken cancellationToken = default);

    Task StepOutAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
