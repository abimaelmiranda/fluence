using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Services.Debugging;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.ViewModels;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;

namespace Fluence.Modules.DebuggerSetup.ViewModels;

public sealed partial class DebuggerSetupViewModel : ViewModelBase
{
    private const string ToolTabId = "tool://fluence/debugger-setup";

    private readonly IDebuggerProvisioningService _provisioning;
    private readonly IWorkspaceContext _workspace;
    private readonly IShellEventBus _eventBus;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _output = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _errorMessage;

    public DebuggerSetupViewModel(
        IDebuggerProvisioningService provisioning,
        IWorkspaceContext workspace,
        IShellEventBus eventBus)
    {
        _provisioning = provisioning;
        _workspace = workspace;
        _eventBus = eventBus;
    }

    public async Task StartProvisioningAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsRunning = true;
            ErrorMessage = null;
            Output = string.Empty;
        });

        try
        {
            await _provisioning.ProvisionAsync(AppendOutput, _cts.Token).ConfigureAwait(false);

            AppendOutput("[Fluence] Setup complete. Starting debug session...");
            await PublishCompletedAsync(startDebugSession: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AppendOutput("[Fluence] Setup cancelled.");
            await SetErrorAsync("Setup was cancelled.").ConfigureAwait(false);
            await PublishCompletedAsync(startDebugSession: false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppendOutput($"[Fluence] Setup failed: {ex.Message}");
            await SetErrorAsync(ex.Message).ConfigureAwait(false);
            await PublishCompletedAsync(startDebugSession: false).ConfigureAwait(false);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsRunning = false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    private void AppendOutput(string line)
    {
        Dispatcher.UIThread.Post(() => Output += line + "\n");
    }

    private async Task SetErrorAsync(string message)
    {
        await Dispatcher.UIThread.InvokeAsync(() => ErrorMessage = message);
    }

    private async Task PublishCompletedAsync(bool startDebugSession)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _eventBus.Publish(new DebuggerProvisioningFinishedEvent());

            if (!startDebugSession)
                return;

            _workspace.CloseDocument(ToolTabId);
            _eventBus.Publish(new DebugProjectRequestedEvent());
        });
    }
}
