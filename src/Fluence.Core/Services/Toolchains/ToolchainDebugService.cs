using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Abstractions.Toolchains;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Toolchains;

namespace Fluence.Core.Services.Toolchains;

public sealed class ToolchainDebugService(
    IToolchainRegistry registry,
    IUserNotificationService notifications) : IDebugService
{
    public Task StartAsync(CancellationToken cancellationToken = default) =>
        WithDebugAsync(async toolchain =>
        {
            if (await toolchain.EnsureAsync(ToolchainCapability.Debugger, cancellationToken).ConfigureAwait(false))
                await toolchain.Debug.StartAsync(cancellationToken).ConfigureAwait(false);
        });

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        WithDebugAsync(toolchain => toolchain.Debug.StopAsync(cancellationToken));

    public Task RestartAsync(CancellationToken cancellationToken = default) =>
        WithDebugAsync(toolchain => toolchain.Debug.RestartAsync(cancellationToken));

    public Task ContinueAsync(CancellationToken cancellationToken = default) =>
        WithDebugAsync(toolchain => toolchain.Debug.ContinueAsync(cancellationToken));

    public Task StepOverAsync(CancellationToken cancellationToken = default) =>
        WithDebugAsync(toolchain => toolchain.Debug.StepOverAsync(cancellationToken));

    public Task StepIntoAsync(CancellationToken cancellationToken = default) =>
        WithDebugAsync(toolchain => toolchain.Debug.StepIntoAsync(cancellationToken));

    public Task StepOutAsync(CancellationToken cancellationToken = default) =>
        WithDebugAsync(toolchain => toolchain.Debug.StepOutAsync(cancellationToken));

    public Task ToggleBreakpointAsync(string filePath, int line, CancellationToken cancellationToken = default) =>
        WithDebugAsync(toolchain => toolchain.Debug.ToggleBreakpointAsync(filePath, line, cancellationToken));

    public Task<DebugVariable?> EvaluateAsync(string expression, CancellationToken cancellationToken = default) =>
        WithDebugResultAsync(toolchain => toolchain.Debug.EvaluateAsync(expression, cancellationToken), null);

    public Task<IReadOnlyList<DebugVariable>> GetChildVariablesAsync(int variablesReference, CancellationToken cancellationToken = default) =>
        WithDebugResultAsync(toolchain => toolchain.Debug.GetChildVariablesAsync(variablesReference, cancellationToken), []);

    private Task WithDebugAsync(Func<IDebugToolchain, Task> action) =>
        registry.Active is IDebugToolchain toolchain
            ? action(toolchain)
            : NotifyUnsupportedAsync();

    private Task<T> WithDebugResultAsync<T>(Func<IDebugToolchain, Task<T>> action, T fallback) =>
        registry.Active is IDebugToolchain toolchain
            ? action(toolchain)
            : NotifyUnsupportedAsync(fallback);

    private Task NotifyUnsupportedAsync()
    {
        notifications.ShowWarning("Debug", $"The active toolchain '{registry.Active.Id}' does not support debugging.");
        return Task.CompletedTask;
    }

    private Task<T> NotifyUnsupportedAsync<T>(T fallback)
    {
        notifications.ShowWarning("Debug", $"The active toolchain '{registry.Active.Id}' does not support debugging.");
        return Task.FromResult(fallback);
    }
}
