using System;
using System.Collections.ObjectModel;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Output;
using Fluence.Core.ViewModels;
using Fluence.Modules.SourceControl.Abstractions;
using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.ViewModels;

public sealed partial class SourceControlViewModel : ViewModelBase, IDisposable
{
    private readonly IGitService _git;
    private readonly ISourceControlDialogService _dialogs;
    private readonly IWorkspaceContext _workspace;
    private readonly IShellEventBus _events;
    private readonly IOutputChannelService _output;
    private System.IO.FileSystemWatcher? _watcher;
    private System.Threading.CancellationTokenSource? _debounce;
    private string? _repoRoot;
    private GitBranch? _pendingCheckoutBranchData;

    public ObservableCollection<GitFileChangeViewModel> StagedChanges { get; } = new();
    public ObservableCollection<GitFileChangeViewModel> UnstagedChanges { get; } = new();
    public ObservableCollection<BranchItemViewModel> Branches { get; } = new();
    public ObservableCollection<StashItemViewModel> Stashes { get; } = new();

    public bool HasStashes => Stashes.Count > 0;
    public bool HasChanges => StagedChanges.Count > 0 || UnstagedChanges.Count > 0;
    public bool HasStagedChanges => StagedChanges.Count > 0;
    public bool HasUnstagedChanges => UnstagedChanges.Count > 0;
    public bool IsWorkingTreeClean => IsGitRepository && !HasChanges;
    public bool HasAhead => AheadCount > 0;
    public bool HasBehind => BehindCount > 0;

    public SourceControlViewModel(
        IGitService git,
        ISourceControlDialogService dialogs,
        IWorkspaceContext workspace,
        IShellEventBus events,
        IOutputChannelService output)
    {
        _git = git;
        _dialogs = dialogs;
        _workspace = workspace;
        _events = events;
        _output = output;
    }

    public void Initialize(string? workspaceRoot)
    {
        WriteOutput("[SourceControl] Initializing\r\n");
        DisposeWatcher();
        _ = InitializeCoreAsync(workspaceRoot);
    }

    public void Dispose()
    {
        _debounce?.Cancel();
        _debounce?.Dispose();
        DisposeWatcher();
    }

    private void WriteOutput(string text, OutputChannelEntryKind kind = OutputChannelEntryKind.Information) =>
        _ = _output.WriteAsync(OutputChannelIds.Output, text, kind);
}
