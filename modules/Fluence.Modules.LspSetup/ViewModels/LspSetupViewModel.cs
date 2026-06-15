using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.ViewModels;

namespace Fluence.Modules.LspSetup.ViewModels;

public sealed partial class LspSetupViewModel : ViewModelBase
{
    private const string ToolTabId = "tool://fluence/lsp-setup";

    private readonly ILspProvisioningService _provisioning;
    private readonly IWorkspaceContext _workspace;
    private readonly IShellEventBus _eventBus;
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
        IShellEventBus eventBus)
    {
        _provisioning = provisioning;
        _workspace = workspace;
        _eventBus = eventBus;
    }

    public async Task StartProvisioningAsync()
    {
        _cts = new CancellationTokenSource();
        IsRunning = true;
        ErrorMessage = null;
        Output = string.Empty;

        try
        {
            await _provisioning.ProvisionAsync(AppendOutput, _cts.Token).ConfigureAwait(false);

            AppendOutput("[Fluence] Setup complete. Starting language server...");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _eventBus.Publish(new LspProvisioningCompletedEvent());
                _workspace.CloseDocument(ToolTabId);
            });
        }
        catch (OperationCanceledException)
        {
            AppendOutput("[Fluence] Setup cancelled.");
            ErrorMessage = "Setup was cancelled.";
        }
        catch (Exception ex)
        {
            AppendOutput($"[Fluence] Setup failed: {ex.Message}");
            ErrorMessage = "Setup failed. Click Retry to try again.";
        }
        finally
        {
            IsRunning = false;
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
        Dispatcher.UIThread.Post(() => Output += line + "\n");
    }
}
