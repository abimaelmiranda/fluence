using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Output;
using Fluence.Core.Services;
using Fluence.Core.ViewModels;
using Fluence.Modules.SourceControl.Abstractions;
using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.ViewModels;

public sealed partial class SourceControlViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromSeconds(5);

    private readonly IGitService _git;
    private readonly ISourceControlDialogService _dialogs;
    private readonly IWorkspaceContext _workspace;
    private readonly IShellEventBus _events;
    private readonly IOutputChannelService _output;
    private readonly ILocalizationService _loc;
    private readonly ISettingsService _settings;
    private IDisposable? _settingsSubscription;
    private System.IO.FileSystemWatcher? _gitIndexWatcher;
    private System.Threading.CancellationTokenSource? _gitIndexDebounce;
    private System.Threading.CancellationTokenSource? _periodicRefreshCts;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private TimeSpan _refreshInterval;
    private bool _disposed;
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

    public string StagedChangesTitle => string.Format(_loc.Get("SourceControl.Section.StagedChanges"), StagedChanges.Count);
    public string UnstagedChangesTitle => string.Format(_loc.Get("SourceControl.Section.Changes"), UnstagedChanges.Count);
    public string StashesTitle => string.Format(_loc.Get("SourceControl.Section.Stashes"), Stashes.Count);

    public SourceControlViewModel(
        IGitService git,
        ISourceControlDialogService dialogs,
        IWorkspaceContext workspace,
        IShellEventBus events,
        IOutputChannelService output,
        ILocalizationService loc,
        ISettingsService settings)
    {
        _git = git;
        _dialogs = dialogs;
        _workspace = workspace;
        _events = events;
        _output = output;
        _loc = loc;
        _settings = settings;
        _refreshInterval = MinimumRefreshInterval;
        loc.LanguageChanged += OnLanguageChanged;
        StagedChanges.CollectionChanged += (_, _) => OnPropertyChanged(nameof(StagedChangesTitle));
        UnstagedChanges.CollectionChanged += (_, _) => OnPropertyChanged(nameof(UnstagedChangesTitle));
        Stashes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(StashesTitle));
    }

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(StagedChangesTitle));
        OnPropertyChanged(nameof(UnstagedChangesTitle));
        OnPropertyChanged(nameof(StashesTitle));
    }

    private void OnSettingsChanged(SourceControlSettings settings)
    {
        var nextInterval = NormalizeRefreshInterval(settings);
        if (nextInterval == _refreshInterval)
            return;

        _refreshInterval = nextInterval;
        if (_repoRoot is not null)
            RestartPeriodicRefreshLoop();
    }

    public void Initialize(string? workspaceRoot)
    {
        WriteOutput("[SourceControl] Initializing\r\n");
        EnsureSettingsSubscription();
        StopGitIndexWatcher();
        StopPeriodicRefreshLoop();
        _ = InitializeCoreAsync(workspaceRoot);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _loc.LanguageChanged -= OnLanguageChanged;
        _settingsSubscription?.Dispose();
        _settingsSubscription = null;
        _gitIndexDebounce?.Cancel();
        _gitIndexDebounce?.Dispose();
        _gitIndexDebounce = null;
        StopPeriodicRefreshLoop();
        StopGitIndexWatcher();
    }

    private void EnsureSettingsSubscription()
    {
        _refreshInterval = NormalizeRefreshInterval(_settings.Get<SourceControlSettings>());
        _settingsSubscription ??= _settings
            .Watch<SourceControlSettings>()
            .Subscribe(new ActionObserver<SourceControlSettings>(OnSettingsChanged));
    }

    private static TimeSpan NormalizeRefreshInterval(SourceControlSettings settings) =>
        TimeSpan.FromSeconds(Math.Max(
            (int)MinimumRefreshInterval.TotalSeconds,
            settings.RefreshIntervalSeconds));

    private void WriteOutput(string text, OutputChannelEntryKind kind = OutputChannelEntryKind.Information) =>
        _ = _output.WriteAsync(OutputChannelIds.Output, text, kind);

    private void FireAndForget(Func<Task> operation) => _ = RunFireAndForgetAsync(operation);

    private async Task RunFireAndForgetAsync(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            WriteOutput($"[SourceControl] Operation failed: {ex.Message}\r\n", OutputChannelEntryKind.Error);
        }
    }
}
