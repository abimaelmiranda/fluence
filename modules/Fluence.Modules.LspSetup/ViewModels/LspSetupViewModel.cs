using System;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.ViewModels;
using Fluence.Core.Events.Provisioning;

namespace Fluence.Modules.LspSetup.ViewModels;

public sealed partial class LspSetupViewModel : ProvisioningSetupViewModelBase
{
    private const string ToolTabId = "tool://fluence/lsp-setup";

    private readonly ILspProvisioningService _provisioning;
    private readonly IWorkspaceContext _workspace;
    private readonly IShellEventBus _eventBus;
    private readonly ILocalizationService _loc;

    public LspSetupViewModel(
        ILspProvisioningService provisioning,
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

    protected override Task ProvisionAsync(Action<string> onOutput, CancellationToken cancellationToken)
        => _provisioning.ProvisionAsync(onOutput, cancellationToken);

    protected override async Task OnProvisioningCompletedAsync()
    {
        await PublishOutputAsync(_loc.Get("LspSetup.Log.SetupComplete")).ConfigureAwait(false);
        _workspace.CloseDocument(ToolTabId);
        _eventBus.Publish(new LspProvisioningCompletedEvent());
    }

    protected override Task OnProvisioningCancelledAsync() =>
        PublishOutputAsync(_loc.Get("LspSetup.Log.SetupCancelled"));

    protected override Task OnProvisioningFailedAsync(Exception exception) =>
        PublishOutputAsync(string.Format(_loc.Get("LspSetup.Log.SetupFailed"), exception.Message));
}
