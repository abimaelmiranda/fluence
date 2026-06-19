using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.ViewModels;
using Fluence.Core.Events.Provisioning;

namespace Fluence.Modules.LspSetup.ViewModels;

public sealed partial class LspSetupViewModel : ViewModelBase
{
    private const string ToolTabId = "tool://fluence/lsp-setup";

    private readonly ILspProvisioningService _provisioning;
    private readonly IWorkspaceContext _workspace;
    private readonly IShellEventBus _eventBus;
    private readonly IUiDispatcher _dispatcher;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _output = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private string? _errorMessage;

    public LspSetupViewModel(
        ILspProvisioningService provisioning,
        IWorkspaceContext workspace,
        IShellEventBus eventBus,
        IUiDispatcher dispatcher)
    {
        _provisioning = provisioning;
        _workspace = workspace;
        _eventBus = eventBus;
        _dispatcher = dispatcher;
    }

    public async Task StartProvisioningAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await _dispatcher.InvokeAsync(() =>
        {
            IsRunning = true;
            ErrorMessage = null;
            Output = string.Empty;
        });

        try
        {
            await _provisioning.ProvisionAsync(AppendOutput, _cts.Token).ConfigureAwait(false);

            AppendOutput("[Fluence] Setup complete. Starting language server...");
            await _dispatcher.InvokeAsync(() =>
            {
                _eventBus.Publish(new LspProvisioningCompletedEvent());
                _workspace.CloseDocument(ToolTabId);
            });
        }
        catch (OperationCanceledException)
        {
            AppendOutput("[Fluence] Setup cancelled.");
            await SetErrorAsync("Setup was cancelled.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppendOutput($"[Fluence] Setup failed: {ex.Message}");
            await SetErrorAsync("Setup failed. Click Retry to try again.").ConfigureAwait(false);
        }
        finally
        {
            await _dispatcher.InvokeAsync(() => IsRunning = false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private async Task Retry()
    {
        await StartProvisioningAsync().ConfigureAwait(false);
    }

    private bool CanRetry() => !IsRunning && ErrorMessage is not null;

    private void AppendOutput(string line)
    {
        _dispatcher.Post(() => Output += line + "\n");
    }

    private async Task SetErrorAsync(string message)
    {
        await _dispatcher.InvokeAsync(() => ErrorMessage = message);
    }
}
