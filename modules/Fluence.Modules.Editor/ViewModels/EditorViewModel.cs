using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluence.Core.Commands;
using Fluence.Core.ViewModels;
using Fluence.Core.Workspace;

namespace Fluence.Modules.Editor.ViewModels;

public sealed partial class EditorViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(750);

    private readonly IWorkspaceContext _workspace;
    private readonly ICommandHandler<SaveActiveDocumentCommand> _saveHandler;
    private readonly DispatcherTimer _autoSaveTimer;
    private bool _isRefreshingFromWorkspace;
    private string? _pendingAutoSavePath;

    [ObservableProperty]
    private string _activeText = string.Empty;

    public EditorViewModel(IWorkspaceContext workspace, ICommandHandler<SaveActiveDocumentCommand> saveHandler)
    {
        _workspace = workspace;
        _saveHandler = saveHandler;
        _autoSaveTimer = new DispatcherTimer { Interval = AutoSaveDelay };
        _autoSaveTimer.Tick += OnAutoSaveTimerTick;
        RefreshFromWorkspace();
        _workspace.Changed += OnWorkspaceChanged;
    }

    public bool HasActiveDocument => _workspace.Current.TabSession.ActiveDocument?.Kind == OpenDocumentKind.TextDocument;

    public string? ActiveDocumentPath => _workspace.Current.TabSession.ActiveDocument?.Kind == OpenDocumentKind.TextDocument
        ? _workspace.Current.TabSession.ActiveDocument.Path
        : null;

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

    private void RefreshFromWorkspace()
    {
        _isRefreshingFromWorkspace = true;
        ActiveText = _workspace.Current.TabSession.ActiveDocument?.Kind == OpenDocumentKind.TextDocument
            ? _workspace.Current.TabSession.ActiveDocument.Content
            : string.Empty;
        _isRefreshingFromWorkspace = false;
        OnPropertyChanged(nameof(HasActiveDocument));
        OnPropertyChanged(nameof(ActiveDocumentPath));
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
    }
}
