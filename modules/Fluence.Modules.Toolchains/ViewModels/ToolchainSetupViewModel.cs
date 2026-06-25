using System;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Events.Debug;
using Fluence.Core.Events.Lsp;
using Fluence.Core.Events.Provisioning;
using Fluence.Core.Events.Toolchains;
using Fluence.Core.Models.Toolchains;
using Fluence.Core.Models.Workspace;
using Fluence.Core.ViewModels;

namespace Fluence.Modules.Toolchains.ViewModels;

public sealed partial class ToolchainSetupViewModel(
    ILspProvisioningService lsp,
    IDebuggerProvisioningService debugger,
    CppDebuggerProvisioningService cppDebugger,
    IWorkspaceContext workspace,
    IShellEventBus events,
    IUiDispatcher dispatcher) : ProvisioningSetupViewModelBase(dispatcher)
{
    public string Title { get; private set; } = "Toolchain Setup";

    public string Subtitle { get; private set; } = string.Empty;

    public string ToolchainId { get; private set; } = "csharp";

    public ToolchainCapability Capability { get; private set; }

    public void Load(string toolchainId, ToolchainCapability capability)
    {
        ToolchainId = toolchainId;
        Capability = capability;
        Title = capability switch
        {
            ToolchainCapability.LanguageServer => "Language Server Setup",
            ToolchainCapability.Debugger => "Debugger Setup",
            _ => "Toolchain Setup",
        };
        Subtitle = capability switch
        {
            ToolchainCapability.LanguageServer => toolchainId == "csharp"
                ? "Installing OmniSharp for C# language features."
                : "Installing language server support.",
            ToolchainCapability.Debugger => toolchainId == "csharp"
                ? "Installing netcoredbg for C# debugging."
                : "Installing debugger support.",
            _ => string.Empty,
        };
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
    }

    protected override Task ProvisionAsync(Action<string> onOutput, CancellationToken cancellationToken) =>
        Capability switch
        {
            ToolchainCapability.LanguageServer => lsp.ProvisionAsync(onOutput, cancellationToken),
            ToolchainCapability.Debugger => GetDebugger().ProvisionAsync(onOutput, cancellationToken),
            _ => Task.CompletedTask,
        };

    protected override async Task OnProvisioningCompletedAsync()
    {
        if (Capability == ToolchainCapability.LanguageServer && !lsp.IsProvisioned())
        {
            await PublishErrorAsync("Language server is still unavailable.").ConfigureAwait(false);
            return;
        }

        if (Capability == ToolchainCapability.Debugger && !GetDebugger().IsProvisioned())
        {
            await PublishErrorAsync("Debugger is still unavailable.").ConfigureAwait(false);
            return;
        }

        workspace.CloseDocument(ToolTabId);
        events.Publish(new ToolchainProvisioningCompletedEvent(ToolchainId, Capability));

        if (Capability == ToolchainCapability.LanguageServer)
            events.Publish(new LspProvisioningCompletedEvent());
        if (Capability == ToolchainCapability.Debugger)
        {
            events.Publish(new DebuggerProvisioningFinishedEvent());
            events.Publish(new DebugProjectRequestedEvent());
        }

    }

    public string ToolTabId =>
        Capability == ToolchainCapability.Debugger
            ? ToolTabIds.DebuggerSetup
            : ToolTabIds.LspSetup;

    private IDebuggerProvisioningService GetDebugger() =>
        ToolchainId is "c" or "cpp" ? cppDebugger : debugger;
}
