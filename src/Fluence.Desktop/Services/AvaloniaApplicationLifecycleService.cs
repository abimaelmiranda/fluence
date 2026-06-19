using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Fluence.Core.Abstractions.Lifecycle;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Lifecycle;
using Fluence.Core.Services.Modules;
using Fluence.Infrastructure;

namespace Fluence.Desktop.Services;

public sealed class AvaloniaApplicationLifecycleService(
    WorkspaceSnapshotCoordinator snapshotCoordinator,
    IShutdownCoordinator shutdownCoordinator) : IApplicationLifecycleService
{
    private static readonly TimeSpan MinimumOverlayDuration = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(15);

    private readonly object _gate = new();
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private Task<ApplicationShutdownOutcome>? _shutdownTask;
    private bool _shutdownInProgress;
    private bool _shutdownComplete;

    public event EventHandler<ApplicationShutdownStatusChangedEventArgs>? StatusChanged;

    public bool IsShutdownInProgress
    {
        get
        {
            lock (_gate)
                return _shutdownInProgress;
        }
    }

    public bool IsShutdownComplete
    {
        get
        {
            lock (_gate)
                return _shutdownComplete;
        }
    }

    public void Attach(IClassicDesktopStyleApplicationLifetime desktop)
    {
        lock (_gate)
            _desktop = desktop;
    }

    public Task<ApplicationShutdownOutcome> RequestShutdownAsync(
        ApplicationShutdownRequest request,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_shutdownTask is not null)
                return _shutdownTask;

            _shutdownInProgress = true;
            _shutdownTask = ShutdownOnceAsync(request, cancellationToken);
            return _shutdownTask;
        }
    }

    private async Task<ApplicationShutdownOutcome> ShutdownOnceAsync(
        ApplicationShutdownRequest request,
        CancellationToken cancellationToken)
    {
        var outcome = ApplicationShutdownOutcome.Completed;

        try
        {
            Report("Saving workspace...");
            if (snapshotCoordinator.HasWorkspaceToSave)
            {
                await Task.WhenAll(
                        snapshotCoordinator.SaveAsync(),
                        Task.Delay(MinimumOverlayDuration, cancellationToken))
                    .ConfigureAwait(false);
            }

            Report("Stopping modules...");
            using var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            shutdownCts.CancelAfter(ShutdownTimeout);
            var progress = new Progress<ModuleShutdownProgress>(p => Report(p.Message));

            await shutdownCoordinator
                .ShutdownAsync(request.Reason, progress, shutdownCts.Token)
                .ConfigureAwait(false);

            Report("Finalizing shutdown...");
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            outcome = ApplicationShutdownOutcome.Failed;
            Debug.WriteLine($"Application shutdown canceled: {ex.Message}");
        }
        catch (OperationCanceledException ex)
        {
            outcome = ApplicationShutdownOutcome.Failed;
            Debug.WriteLine($"Application shutdown timed out after {ShutdownTimeout.TotalSeconds}s: {ex.Message}");
        }
        catch (Exception ex)
        {
            outcome = ApplicationShutdownOutcome.Failed;
            Debug.WriteLine($"Application shutdown failed: {ex}");
        }
        finally
        {
            lock (_gate)
            {
                _shutdownInProgress = false;
                _shutdownComplete = true;
            }

            if (request.RequestNativeShutdown)
                ShutdownAvalonia(request.ExitCode);
        }

        return outcome;
    }

    private void Report(string message) =>
        StatusChanged?.Invoke(this, new ApplicationShutdownStatusChangedEventArgs(message));

    private void ShutdownAvalonia(int exitCode)
    {
        IClassicDesktopStyleApplicationLifetime? desktop;
        lock (_gate)
            desktop = _desktop;

        if (desktop is null)
            return;

        if (Dispatcher.UIThread.CheckAccess())
        {
            desktop.Shutdown(exitCode);
            return;
        }

        Dispatcher.UIThread.Post(() => desktop.Shutdown(exitCode), DispatcherPriority.Send);
    }
}
