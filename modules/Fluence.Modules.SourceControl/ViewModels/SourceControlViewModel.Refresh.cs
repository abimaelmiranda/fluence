using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Output;
using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.ViewModels;

public sealed partial class SourceControlViewModel
{
    private async Task InitializeCoreAsync(string? workspaceRoot)
    {
        if (string.IsNullOrEmpty(workspaceRoot))
        {
            SetNoRepository();
            return;
        }

        var root = await _git.GetRepositoryRootAsync(workspaceRoot);
        if (root is null)
        {
            WriteOutput("[SourceControl] No Git repository found\r\n", OutputChannelEntryKind.Warning);
            SetNoRepository();
            return;
        }

        _repoRoot = root;
        IsGitRepository = true;
        WriteOutput($"[SourceControl] Repository: {root}\r\n");
        await RefreshCoreAsync();
        StartGitIndexWatcher(Path.Combine(root, ".git"));
        RestartPeriodicRefreshLoop();
    }

    [RelayCommand]
    private async Task RefreshAsync()
        => await RefreshCoreAsync();

    private async Task RefreshCoreAsync(bool writeLog = true, bool skipIfBusy = false)
    {
        var repoRoot = _repoRoot;
        if (repoRoot is null) return;
        if (skipIfBusy)
        {
            if (!await _refreshGate.WaitAsync(0).ConfigureAwait(false))
                return;
        }
        else
        {
            await _refreshGate.WaitAsync().ConfigureAwait(false);
        }

        try
        {
            if (writeLog)
                WriteOutput("[SourceControl] Refreshing status\r\n");

            var statusTask = _git.GetStatusAsync(repoRoot);
            var branchesTask = _git.GetBranchesAsync(repoRoot);
            var aheadBehindTask = _git.GetAheadBehindAsync(repoRoot);
            var stashTask = _git.GetStashListAsync(repoRoot);

            await Task.WhenAll(statusTask, branchesTask, aheadBehindTask, stashTask);

            var status = statusTask.Result;
            var branches = branchesTask.Result;
            var aheadBehind = aheadBehindTask.Result;
            var stashes = stashTask.Result;

            Dispatcher.UIThread.Post(() =>
            {
                if (!string.Equals(_repoRoot, repoRoot, StringComparison.OrdinalIgnoreCase))
                    return;

                ApplyStatus(status);
                ApplyBranches(branches);
                ApplyStashes(stashes);
                AheadCount = aheadBehind.Ahead;
                BehindCount = aheadBehind.Behind;
            });
        }
        catch (OperationCanceledException ex)
        {
            Debug.WriteLine($"Source control refresh canceled: {ex}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Source control refresh failed: {ex}");
            WriteOutput($"[SourceControl] Refresh failed: {ex.Message}\r\n", OutputChannelEntryKind.Error);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void ApplyStatus(GitStatus status)
    {
        CurrentBranch = status.Branch;

        StagedChanges.Clear();
        foreach (var change in status.StagedChanges)
            StagedChanges.Add(new GitFileChangeViewModel(change, StageFileAsync, UnstageFileAsync, OpenDiffAsync, OpenFileAsync, RevertFileAsync));

        UnstagedChanges.Clear();
        foreach (var change in status.UnstagedChanges)
            UnstagedChanges.Add(new GitFileChangeViewModel(change, StageFileAsync, UnstageFileAsync, OpenDiffAsync, OpenFileAsync, RevertFileAsync));

        NotifyStatusPropertiesChanged();
        CommitCommand.NotifyCanExecuteChanged();
    }

    private void SetNoRepository()
    {
        StopGitIndexWatcher();
        StopPeriodicRefreshLoop();
        _repoRoot = null;
        IsGitRepository = false;
        CurrentBranch = string.Empty;
        AheadCount = 0;
        BehindCount = 0;

        Dispatcher.UIThread.Post(() =>
        {
            StagedChanges.Clear();
            UnstagedChanges.Clear();
            Branches.Clear();
            Stashes.Clear();
            NotifyStatusPropertiesChanged();
            OnPropertyChanged(nameof(HasStashes));
        });
    }

    private void NotifyStatusPropertiesChanged()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasStagedChanges));
        OnPropertyChanged(nameof(HasUnstagedChanges));
        OnPropertyChanged(nameof(IsWorkingTreeClean));
    }

    private void StartGitIndexWatcher(string gitDirectory)
    {
        if (!Directory.Exists(gitDirectory)) return;

        _gitIndexWatcher = new FileSystemWatcher(gitDirectory, "index")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _gitIndexWatcher.Changed += OnGitIndexChanged;
    }

    private void OnGitIndexChanged(object sender, FileSystemEventArgs e)
    {
        var previousDebounce = _gitIndexDebounce;
        previousDebounce?.Cancel();
        var debounce = new CancellationTokenSource();
        _gitIndexDebounce = debounce;
        var token = debounce.Token;

        _ = Task.Delay(500, token).ContinueWith(t =>
        {
            try
            {
                if (!t.IsCanceled) _ = RefreshCoreAsync(writeLog: false, skipIfBusy: true);
            }
            finally
            {
                if (ReferenceEquals(_gitIndexDebounce, debounce))
                    _gitIndexDebounce = null;
                debounce.Dispose();
            }
        }, TaskScheduler.Default);
    }

    private void RestartPeriodicRefreshLoop()
    {
        StopPeriodicRefreshLoop();

        if (_repoRoot is null)
            return;

        _periodicRefreshCts = new CancellationTokenSource();
        _ = RunPeriodicRefreshLoopAsync(_periodicRefreshCts.Token);
    }

    private async Task RunPeriodicRefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(_refreshInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await RefreshCoreAsync(writeLog: false, skipIfBusy: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Source control periodic refresh failed: {ex}");
            WriteOutput($"[SourceControl] Periodic refresh stopped: {ex.Message}\r\n", OutputChannelEntryKind.Error);
        }
    }

    private void StopPeriodicRefreshLoop()
    {
        if (_periodicRefreshCts is null)
            return;

        _periodicRefreshCts.Cancel();
        _periodicRefreshCts.Dispose();
        _periodicRefreshCts = null;
    }

    private void StopGitIndexWatcher()
    {
        var debounce = _gitIndexDebounce;
        _gitIndexDebounce = null;
        debounce?.Cancel();

        if (_gitIndexWatcher is null) return;

        _gitIndexWatcher.EnableRaisingEvents = false;
        _gitIndexWatcher.Changed -= OnGitIndexChanged;
        _gitIndexWatcher.Dispose();
        _gitIndexWatcher = null;
    }
}
