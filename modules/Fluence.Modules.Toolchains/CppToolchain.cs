using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Abstractions.Toolchains;
using Fluence.Core.Events.Toolchains;
using Fluence.Core.Models.Toolchains;

namespace Fluence.Modules.Toolchains;

public sealed class CppToolchain(
    string languageId,
    CppRunService runService,
    CppDebugService debug,
    CppDebuggerProvisioningService debugger,
    IShellEventBus events,
    IUserNotificationService notifications) : IDebugToolchain
{
    public string Id => languageId;

    public string LanguageId => languageId;

    public ToolchainSupportLevel SupportLevel => ToolchainSupportLevel.Experimental;

    public ToolchainCapabilities Capabilities { get; } = new(ToolchainCapability.Run | ToolchainCapability.Debugger);

    public IDebugService Debug => debug;

    public Task<bool> EnsureAsync(
        ToolchainCapability capability,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        if (!Capabilities.Has(capability))
        {
            notifications.ShowWarning(
                "C/C++",
                $"Capability '{capability}' is not wired for the experimental C/C++ toolchain yet.");
            return Task.FromResult(false);
        }

        if (capability.HasFlag(ToolchainCapability.Debugger) && !debugger.IsProvisioned())
        {
            events.Publish(new ToolchainProvisioningRequiredEvent(Id, ToolchainCapability.Debugger));
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public async Task ExecuteAsync(ToolchainCommand command, CancellationToken cancellationToken = default)
    {
        switch (command.Kind)
        {
            case ToolchainCommandKind.RunProject:
                await runService.RunAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.RunSpecificProject:
                await runService.RunSpecificAsync(RequireProjectPath(command), cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.DebugProject:
                if (await EnsureAsync(ToolchainCapability.Debugger, cancellationToken).ConfigureAwait(false))
                    await Debug.StartAsync(cancellationToken).ConfigureAwait(false);
                break;
            default:
                notifications.ShowWarning(
                    "C/C++",
                    $"'{command.Kind}' is not supported yet by the experimental C/C++ toolchain.");
                break;
        }
    }

    private static string RequireProjectPath(ToolchainCommand command) =>
        string.IsNullOrWhiteSpace(command.ProjectPath)
            ? throw new ArgumentException("Project path is required.", nameof(command))
            : command.ProjectPath;
}
