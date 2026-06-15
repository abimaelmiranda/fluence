using Fluence.Core.Models.Debugging;

namespace Fluence.Core.Abstractions.Debugging;

public interface IDebugService
{
    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task RestartAsync(CancellationToken cancellationToken = default);

    Task ContinueAsync(CancellationToken cancellationToken = default);

    Task StepOverAsync(CancellationToken cancellationToken = default);

    Task StepIntoAsync(CancellationToken cancellationToken = default);

    Task StepOutAsync(CancellationToken cancellationToken = default);

    Task ToggleBreakpointAsync(string filePath, int line, CancellationToken cancellationToken = default);

    Task<DebugVariable?> EvaluateAsync(string expression, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DebugVariable>> GetChildVariablesAsync(int variablesReference, CancellationToken cancellationToken = default);
}
