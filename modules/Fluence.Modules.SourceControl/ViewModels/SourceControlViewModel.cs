using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.ViewModels;
using Fluence.Modules.SourceControl.Abstractions;
using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.ViewModels;

public sealed partial class SourceControlViewModel : ViewModelBase, IDisposable
{
    private readonly IGitService _git;
    private readonly IWorkspaceContext _workspace;
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _debounce;
    private string? _repoRoot;

    [ObservableProperty]
    private bool _isGitRepository;

    [ObservableProperty]
    private string _currentBranch = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    private string _commitMessage = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<GitFileChangeViewModel> StagedChanges { get; } = new();
    public ObservableCollection<GitFileChangeViewModel> UnstagedChanges { get; } = new();

    public bool HasChanges          => StagedChanges.Count > 0 || UnstagedChanges.Count > 0;
    public bool HasStagedChanges    => StagedChanges.Count > 0;
    public bool HasUnstagedChanges  => UnstagedChanges.Count > 0;
    public bool IsWorkingTreeClean  => IsGitRepository && !HasChanges;

    public SourceControlViewModel(IGitService git, IWorkspaceContext workspace)
    {
        _git       = git;
        _workspace = workspace;
    }

    public void Initialize(string? workspaceRoot)
    {
        DisposeWatcher();
        _ = InitializeCoreAsync(workspaceRoot);
    }

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
            SetNoRepository();
            return;
        }

        _repoRoot = root;
        IsGitRepository = true;
        await RefreshCoreAsync();
        StartWatcher(Path.Combine(root, ".git"));
    }

    [RelayCommand]
    private async Task RefreshAsync()
        => await RefreshCoreAsync();

    private async Task RefreshCoreAsync()
    {
        if (_repoRoot is null) return;
        try
        {
            var status = await _git.GetStatusAsync(_repoRoot);
            Dispatcher.UIThread.Post(() => ApplyStatus(status));
        }
        catch { /* git not available or repo gone */ }
    }

    [RelayCommand(CanExecute = nameof(CanCommit))]
    private async Task CommitAsync()
    {
        if (_repoRoot is null) return;
        IsBusy = true;
        try
        {
            await _git.CommitAsync(CommitMessage, _repoRoot);
            CommitMessage = string.Empty;
            await RefreshCoreAsync();
        }
        finally { IsBusy = false; }
    }

    private bool CanCommit() => !string.IsNullOrWhiteSpace(CommitMessage) && StagedChanges.Count > 0;

    [RelayCommand]
    private async Task StageAllAsync()
    {
        if (_repoRoot is null) return;
        foreach (var vm in UnstagedChanges.ToArray())
            await _git.StageAsync(vm.FilePath, _repoRoot);
        await RefreshCoreAsync();
    }

    [RelayCommand]
    private async Task UnstageAllAsync()
    {
        if (_repoRoot is null) return;
        foreach (var vm in StagedChanges.ToArray())
            await _git.UnstageAsync(vm.FilePath, _repoRoot);
        await RefreshCoreAsync();
    }

    [RelayCommand]
    private async Task PullAsync()
    {
        if (_repoRoot is null) return;
        IsBusy = true;
        try { await _git.PullAsync(_repoRoot); await RefreshCoreAsync(); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task PushAsync()
    {
        if (_repoRoot is null) return;
        IsBusy = true;
        try { await _git.PushAsync(_repoRoot); }
        finally { IsBusy = false; }
    }

    private async void StageFile(GitFileChange change)
    {
        if (_repoRoot is null) return;
        await _git.StageAsync(change.FilePath, _repoRoot);
        await RefreshCoreAsync();
    }

    private async void UnstageFile(GitFileChange change)
    {
        if (_repoRoot is null) return;
        await _git.UnstageAsync(change.FilePath, _repoRoot);
        await RefreshCoreAsync();
    }

    private async void OpenDiff(GitFileChange change)
    {
        if (_repoRoot is null) return;
        var diff = await _git.GetDiffAsync(change.FilePath, change.IsStaged, _repoRoot);
        var vm = new DiffViewerViewModel($"Diff: {change.FileName}", diff);
        _workspace.OpenToolTab($"diff:{change.FilePath}:{change.IsStaged}", vm.Title, vm);
    }

    private void ApplyStatus(GitStatus status)
    {
        CurrentBranch = status.Branch;

        StagedChanges.Clear();
        foreach (var c in status.StagedChanges)
            StagedChanges.Add(new GitFileChangeViewModel(c, StageFile, UnstageFile, OpenDiff));

        UnstagedChanges.Clear();
        foreach (var c in status.UnstagedChanges)
            UnstagedChanges.Add(new GitFileChangeViewModel(c, StageFile, UnstageFile, OpenDiff));

        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(HasStagedChanges));
        OnPropertyChanged(nameof(HasUnstagedChanges));
        OnPropertyChanged(nameof(IsWorkingTreeClean));
        CommitCommand.NotifyCanExecuteChanged();
    }

    private void SetNoRepository()
    {
        _repoRoot = null;
        IsGitRepository = false;
        CurrentBranch = string.Empty;
        Dispatcher.UIThread.Post(() =>
        {
            StagedChanges.Clear();
            UnstagedChanges.Clear();
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(HasStagedChanges));
            OnPropertyChanged(nameof(HasUnstagedChanges));
            OnPropertyChanged(nameof(IsWorkingTreeClean));
        });
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

    public void Dispose()
    {
        _debounce?.Cancel();
        _debounce?.Dispose();
        DisposeWatcher();
    }
}
