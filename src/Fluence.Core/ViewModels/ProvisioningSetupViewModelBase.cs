using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Tasks;

namespace Fluence.Core.ViewModels;

public abstract partial class ProvisioningSetupViewModelBase(IUiDispatcher dispatcher) : ViewModelBase
{
    private readonly IUiDispatcher _dispatcher = dispatcher;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _output = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private string? _errorMessage;

    public Task StartProvisioningAsync(CancellationToken cancellationToken = default) =>
        RunProvisioningAsync(cancellationToken);

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private async Task Retry()
    {
        await RunProvisioningAsync().ConfigureAwait(false);
    }

    protected Task PublishOutputAsync(string line) =>
        _dispatcher.InvokeAsync(() => Output += line + Environment.NewLine);

    protected Task PublishErrorAsync(string message) =>
        _dispatcher.InvokeAsync(() => ErrorMessage = message);

    private void AppendOutput(string line) =>
        _ = PublishOutputAsync(line);

    protected abstract Task ProvisionAsync(Action<string> onOutput, CancellationToken cancellationToken);

    protected virtual Task OnProvisioningCompletedAsync() => Task.CompletedTask;

    protected virtual Task OnProvisioningCancelledAsync() => Task.CompletedTask;

    protected virtual Task OnProvisioningFailedAsync(Exception exception) => Task.CompletedTask;

    private bool CanRetry() => !IsRunning && ErrorMessage is not null;

    private async Task RunProvisioningAsync(CancellationToken cancellationToken = default)
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
            await ProvisionAsync(AppendOutput, _cts.Token).ConfigureAwait(false);
            await OnProvisioningCompletedAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await OnProvisioningCancelledAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await PublishErrorAsync(ex.Message).ConfigureAwait(false);
            await OnProvisioningFailedAsync(ex).ConfigureAwait(false);
        }
        finally
        {
            await _dispatcher.InvokeAsync(() => IsRunning = false);
            _cts?.Dispose();
            _cts = null;
        }
    }
}
