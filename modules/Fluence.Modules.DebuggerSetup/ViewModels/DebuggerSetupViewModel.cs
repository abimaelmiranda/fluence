using System;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Events.Debug;
using Fluence.Core.Events.Provisioning;
using Fluence.Core.ViewModels;

namespace Fluence.Modules.DebuggerSetup.ViewModels;

public sealed partial class DebuggerSetupViewModel : ProvisioningSetupViewModelBase
{
    private const string ToolTabId = "tool://fluence/debugger-setup";

    private readonly IDebuggerProvisioningService _provisioning;
    private readonly IWorkspaceContext _workspace;
    private readonly IShellEventBus _eventBus;
    private readonly ILocalizationService _loc;

    public DebuggerSetupViewModel(
        IDebuggerProvisioningService provisioning,
        IWorkspaceContext workspace,
        IShellEventBus eventBus,
        IUiDispatcher dispatcher,
        ILocalizationService loc)
        : base(dispatcher)
    {
        _provisioning = provisioning;
        _workspace = workspace;
        _eventBus = eventBus;
        _loc = loc;
    }

    protected override Task ProvisionAsync(Action<string> onOutput, CancellationToken cancellationToken) =>
        _provisioning.ProvisionAsync(onOutput, cancellationToken);

    protected override async Task OnProvisioningCompletedAsync()
    {
        if (!_provisioning.IsProvisioned())
        {
            await PublishOutputAsync(_loc.Get("DebuggerSetup.Log.DebuggerStillUnavailable")).ConfigureAwait(false);
            await PublishErrorAsync(_loc.Get("DebuggerSetup.Error.DebuggerStillUnavailable")).ConfigureAwait(false);
            return;
        }

        await PublishOutputAsync(_loc.Get("DebuggerSetup.Log.SetupComplete")).ConfigureAwait(false);
        _workspace.CloseDocument(ToolTabId);
        _eventBus.Publish(new DebuggerProvisioningFinishedEvent());
        _eventBus.Publish(new DebugProjectRequestedEvent());
    }

    protected override Task OnProvisioningCancelledAsync() =>
        PublishOutputAsync(_loc.Get("DebuggerSetup.Log.SetupCancelled"));

    protected override Task OnProvisioningFailedAsync(Exception exception) =>
        PublishOutputAsync(string.Format(_loc.Get("DebuggerSetup.Log.SetupFailed"), exception.Message));
}
