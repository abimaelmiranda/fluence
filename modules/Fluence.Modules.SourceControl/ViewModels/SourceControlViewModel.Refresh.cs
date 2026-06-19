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
        StartWatcher(Path.Combine(root, ".git"));
    }

    [RelayCommand]
    private async Task RefreshAsync()
        => await RefreshCoreAsync();

    private async Task RefreshCoreAsync()
    {
        var repoRoot = _repoRoot;
        if (repoRoot is null) return;

        try
        {
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

    private void StartWatcher(string gitDirectory)
    {
        if (!Directory.Exists(gitDirectory)) return;

        _watcher = new FileSystemWatcher(gitDirectory, "index")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnGitIndexChanged;
    }

    private void OnGitIndexChanged(object sender, FileSystemEventArgs e)
    {
        _debounce?.Cancel();
        _debounce = new CancellationTokenSource();
        var token = _debounce.Token;

        _ = Task.Delay(500, token).ContinueWith(t =>
        {
            if (!t.IsCanceled) _ = RefreshCoreAsync();
        }, TaskScheduler.Default);
    }

    private void DisposeWatcher()
    {
        if (_watcher is null) return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnGitIndexChanged;
        _watcher.Dispose();
        _watcher = null;
    }
}
