using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Projects;
using Fluence.Core.Abstractions.Toolchains;
using Fluence.Core.Events.Toolchains;
using Fluence.Core.Models.Toolchains;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.Toolchains;

public sealed class RoutedToolchain(
    string id,
    IServiceProvider services,
    IShellEventBus events) : IToolchain
{
    public string Id => id;

    public string LanguageId => id;

    public ToolchainSupportLevel SupportLevel => ToolchainSupportLevel.Experimental;

    public ToolchainCapabilities Capabilities { get; } = new(
        ToolchainCapability.Run |
        ToolchainCapability.LanguageServer |
        ToolchainCapability.Debugger);

    public Task<bool> EnsureAsync(ToolchainCapability capability, CancellationToken cancellationToken = default)
    {
        if (capability.HasFlag(ToolchainCapability.LanguageServer) &&
            !services.GetRequiredService<ILspProvisioningService>().IsProvisioned())
        {
            events.Publish(new ToolchainProvisioningRequiredEvent(Id, ToolchainCapability.LanguageServer));
            return Task.FromResult(false);
        }

        if (capability.HasFlag(ToolchainCapability.Debugger) &&
            !services.GetRequiredService<IDebuggerProvisioningService>().IsProvisioned())
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
                await services.GetRequiredService<IRunService>().RunAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.RunSpecificProject:
                if (!string.IsNullOrWhiteSpace(command.ProjectPath))
                    await services.GetRequiredService<IRunService>()
                        .RunSpecificAsync(command.ProjectPath, cancellationToken)
                        .ConfigureAwait(false);
                break;
            case ToolchainCommandKind.DebugProject:
                if (await EnsureAsync(ToolchainCapability.Debugger, cancellationToken).ConfigureAwait(false))
                    await services.GetRequiredService<IDebugService>().StartAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.StartLanguageServer:
                await EnsureAsync(ToolchainCapability.LanguageServer, cancellationToken).ConfigureAwait(false);
                break;
        }
    }
}
