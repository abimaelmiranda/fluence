using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluence.Core.Debug;
using Fluence.Core.Commands;
using Fluence.Core.Modules;
using Fluence.Core.ViewModels;
using Fluence.Core.Workspace;

namespace Fluence.Modules.Editor.ViewModels;

public sealed partial class EditorViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(750);

    private readonly IWorkspaceContext _workspace;
    private readonly ICommandHandler<SaveActiveDocumentCommand> _saveHandler;
    private readonly IDebugStateService _debugState;
    private readonly IDebugService _debugService;
    private readonly IShellEventBus _events;
    private readonly DispatcherTimer _autoSaveTimer;
    private bool _isRefreshingFromWorkspace;
    private string? _pendingAutoSavePath;

    [ObservableProperty]
    private string _activeText = string.Empty;

    public EditorViewModel(
        IWorkspaceContext workspace,
        ICommandHandler<SaveActiveDocumentCommand> saveHandler,
        IDebugStateService debugState,
        IDebugService debugService,
        IShellEventBus events)
    {
        _workspace = workspace;
        _saveHandler = saveHandler;
        _debugState = debugState;
        _debugService = debugService;
        _events = events;
        _autoSaveTimer = new DispatcherTimer { Interval = AutoSaveDelay };
        _autoSaveTimer.Tick += OnAutoSaveTimerTick;
        RefreshFromWorkspace();
        _workspace.Changed += OnWorkspaceChanged;
        _debugState.Changed += OnDebugStateChanged;
    }

    public bool HasActiveDocument => _workspace.Current.TabSession.ActiveDocument?.Kind == OpenDocumentKind.TextDocument;

    public string? ActiveDocumentPath => _workspace.Current.TabSession.ActiveDocument?.Kind == OpenDocumentKind.TextDocument
        ? _workspace.Current.TabSession.ActiveDocument.Path
        : null;

    public IReadOnlyList<DebugBreakpoint> ActiveDocumentBreakpoints =>
        string.IsNullOrWhiteSpace(ActiveDocumentPath)
            ? Array.Empty<DebugBreakpoint>()
            : _debugState.Snapshot.Breakpoints
                .Where(b => string.Equals(b.FilePath, ActiveDocumentPath, StringComparison.OrdinalIgnoreCase))
                .ToArray();

    public DebugExecutionLine? ActiveExecutionLine
    {
        get
        {
            var currentLine = _debugState.Snapshot.CurrentLine;
            return currentLine is not null &&
                   string.Equals(currentLine.FilePath, ActiveDocumentPath, StringComparison.OrdinalIgnoreCase)
                ? currentLine
                : null;
        }
    }

    public bool IsDebuggerStopped => _debugState.Snapshot.IsStopped;

    public Task<string?> EvaluateHoverAsync(string expression, CancellationToken cancellationToken) =>
        _debugService.EvaluateAsync(expression, cancellationToken);

    public void ToggleBreakpoint(int line)
    {
        if (string.IsNullOrWhiteSpace(ActiveDocumentPath) || line <= 0)
            return;

        _events.Publish(new ToggleBreakpointRequestedEvent(ActiveDocumentPath, line));
    }

    public async Task SaveIfDirtyAsync()
    {
        await SaveIfDirtyAsync(expectedPath: null, CancellationToken.None);
    }

    partial void OnActiveTextChanged(string value)
    {
        if (_isRefreshingFromWorkspace) return;
        _workspace.UpdateActiveDocumentContent(value);
        ScheduleAutoSave(ActiveDocumentPath);
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        CancelPendingAutoSave();
        RefreshFromWorkspace();
    }

    private void OnDebugStateChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshDebugStateProperties();
            return;
        }

        Dispatcher.UIThread.Post(RefreshDebugStateProperties);
    }

    private void RefreshDebugStateProperties()
    {
        OnPropertyChanged(nameof(ActiveDocumentBreakpoints));
        OnPropertyChanged(nameof(ActiveExecutionLine));
    }

    private void RefreshFromWorkspace()
    {
        _isRefreshingFromWorkspace = true;
        ActiveText = _workspace.Current.TabSession.ActiveDocument?.Kind == OpenDocumentKind.TextDocument
            ? _workspace.Current.TabSession.ActiveDocument.Content
            : string.Empty;
        _isRefreshingFromWorkspace = false;
        OnPropertyChanged(nameof(HasActiveDocument));
        OnPropertyChanged(nameof(ActiveDocumentPath));
        OnPropertyChanged(nameof(ActiveDocumentBreakpoints));
        OnPropertyChanged(nameof(ActiveExecutionLine));
    }

    private void ScheduleAutoSave(string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(documentPath))
            return;

        CancelPendingAutoSave();
        _pendingAutoSavePath = documentPath;
        _autoSaveTimer.Start();
    }

    private void OnAutoSaveTimerTick(object? sender, EventArgs e)
    {
        _autoSaveTimer.Stop();

        var documentPath = _pendingAutoSavePath;
        _pendingAutoSavePath = null;

        if (!string.IsNullOrWhiteSpace(documentPath))
        {
            _ = SaveAfterDebounceAsync(documentPath);
        }
    }

    private async Task SaveAfterDebounceAsync(string expectedPath)
    {
        try
        {
            await SaveIfDirtyAsync(expectedPath, CancellationToken.None);
        }
        catch
        {
        }
    }

    private async Task SaveIfDirtyAsync(string? expectedPath, CancellationToken cancellationToken)
    {
        var activeDocument = _workspace.Current.TabSession.ActiveDocument;
        if (activeDocument is not { Kind: OpenDocumentKind.TextDocument, IsDirty: true })
            return;

        if (!string.IsNullOrWhiteSpace(expectedPath) &&
            !string.Equals(activeDocument.Path, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await _saveHandler.HandleAsync(new SaveActiveDocumentCommand(), cancellationToken);
    }

    private void CancelPendingAutoSave()
    {
        _pendingAutoSavePath = null;
        _autoSaveTimer.Stop();
    }

    public void Dispose()
    {
        CancelPendingAutoSave();
        _autoSaveTimer.Tick -= OnAutoSaveTimerTick;
        _workspace.Changed -= OnWorkspaceChanged;
        _debugState.Changed -= OnDebugStateChanged;
    }
}
