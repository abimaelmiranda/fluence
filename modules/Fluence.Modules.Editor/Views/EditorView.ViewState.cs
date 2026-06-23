using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.Document;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Modules.Editor.Services;
using Fluence.Modules.Editor.ViewModels;
using Fluence.Core.Events.Document;
using Fluence.Core.Events.Lsp;

namespace Fluence.Modules.Editor.Views;

public partial class EditorView
{
    private void EnsureEditorTimers()
    {
        _textSyncTimer ??= new Timer(
            static state => ((EditorView)state!).OnTextSyncTimerElapsed(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        _viewStateSaveTimer ??= new Timer(
            static state => ((EditorView)state!).OnViewStateSaveTimerElapsed(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    private void ReleaseEditorRuntimeSubscriptions()
    {
        _textSyncTimer?.Dispose();
        _textSyncTimer = null;
        _viewStateSaveTimer?.Dispose();
        _viewStateSaveTimer = null;
        _completionTimer?.Dispose();
        _completionTimer = null;
        _completionRefreshTimer?.Stop();
        _hoverTimer?.Dispose();
        _hoverTimer = null;
        _popupCloseTimer?.Dispose();
        _popupCloseTimer = null;

        _eventBus?.UnsubscribeSync<DocumentClosedEvent>(OnDocumentClosed);
        _eventBus?.UnsubscribeSync<DiagnosticsUpdatedEvent>(OnDiagnosticsUpdated);
        _eventBus?.UnsubscribeSync<NavigationResolvedEvent>(OnNavigationResolved);
        _eventBus?.UnsubscribeSync<SemanticTokensUpdatedEvent>(OnSemanticTokensUpdated);
        _eventBus?.UnsubscribeSync<WorkspaceEditRequestedEvent>(OnWorkspaceEditRequested);
        _eventBus = null;

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _editorSettingsSubscription?.Dispose();
        _editorSettingsSubscription = null;
        _keybindingsSubscription?.Dispose();
        _keybindingsSubscription = null;
        _themeSubscription?.Dispose();
        _themeSubscription = null;
        _completionService = null;
        _hoverService = null;
        _signatureHelpService = null;
        _codeActionService = null;
        _viewModel = null;

        if (_editorScrollViewer is not null)
        {
            _editorScrollViewer.ScrollChanged -= OnEditorScrollChanged;
            _editorScrollViewer = null;
        }

        ClearEditorTransientState();
    }

    private void ClearEditorTransientState()
    {
        _pendingCompletionRequest = null;
        _completionRequestInFlight = false;
        _completionRefreshPending = false;
        _activeCompletions = null;
        _completionTriggerOffset = -1;
        _pendingHoverRequest = null;
        _hoverRequestInFlight = false;
        _pendingSemanticTokens = null;
        _pendingSemanticTokensPath = null;
        _pendingSemanticTokensVersion = 0;
        _semanticRedrawPending = 0;
        _pendingCodeActionDiag = null;
        _semanticTokensByPath.Clear();
        _pendingViewStateSavePath = null;
        _pendingViewStateSaveTransitionVersion = 0;
        _savedViewStateDuringTextSwitchPath = null;
        _dismissedExceptionPopupKey = null;
        _semanticColorizer.Clear();
        _diagnosticRenderer.Clear();
        Editor.TextArea.Caret.PositionChanged -= OnCaretPositionChangedForCompletion;
        Editor.TextArea.Caret.PositionChanged -= OnCaretPositionChangedForSignatureHelp;
        CompletionPopup.IsOpen = false;
        HoverPopup.IsOpen = false;
        LspHoverPopup.IsOpen = false;
        DiagnosticTooltipPopup.IsOpen = false;
        CodeActionPopup.IsOpen = false;
        SignatureHelpPopup.IsOpen = false;
        CompletionListBox.ItemsSource = null;
        HoverTree.ItemsSource = null;
        CodeActionsListBox.ItemsSource = null;
        Editor.TextArea.TextView.Redraw();
    }

    private void EnsureEditorScrollViewerSubscription()
    {
        var scrollViewer = FindEditorScrollViewer();
        if (ReferenceEquals(scrollViewer, _editorScrollViewer))
            return;

        if (_editorScrollViewer is not null)
            _editorScrollViewer.ScrollChanged -= OnEditorScrollChanged;

        _editorScrollViewer = scrollViewer;
        if (_editorScrollViewer is not null)
            _editorScrollViewer.ScrollChanged += OnEditorScrollChanged;
    }

    private ScrollViewer? GetEditorScrollViewer()
    {
        if (_editorScrollViewer is null)
            EnsureEditorScrollViewerSubscription();

        return _editorScrollViewer;
    }

    private ScrollViewer? FindEditorScrollViewer() =>
        Editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private void OnEditorScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var path = _viewModel?.ActiveDocumentPath;
        if (_isUpdatingEditorText || _isSwitchingDocumentViewState || path is null)
            return;

        _pendingViewStateSavePath = path;
        _pendingViewStateSaveTransitionVersion = _viewStateTransitionVersion;
        _viewStateSaveTimer?.Change(ViewStateSaveDelay, Timeout.InfiniteTimeSpan);
    }

    private void OnViewStateSaveTimerElapsed()
    {
        var path = _pendingViewStateSavePath;
        var transitionVersion = _pendingViewStateSaveTransitionVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel?.ActiveDocumentPath is null ||
                _isUpdatingEditorText ||
                _isSwitchingDocumentViewState ||
                transitionVersion != _viewStateTransitionVersion ||
                !string.Equals(_viewModel.ActiveDocumentPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SaveViewStateForPath(path);
        }, DispatcherPriority.Background);
    }

    private void SwitchEditorDocument(string? newPath, string newText)
    {
        BeginDocumentViewStateSwitch();

        if (!string.Equals(_savedViewStateDuringTextSwitchPath, _lastKnownDocumentPath, StringComparison.OrdinalIgnoreCase))
            SaveViewStateForPath(_lastKnownDocumentPath);

        _savedViewStateDuringTextSwitchPath = null;
        _lastKnownDocumentPath = newPath;
        _pendingSemanticTokens = null;
        _pendingSemanticTokensPath = null;
        _pendingSemanticTokensVersion = 0;
        _semanticColorizer.Clear();
        _diagnosticRenderer.Clear();
        InvalidateHoverRequests();
        InvalidateLspHoverRequests();
        InvalidateCodeActionRequests(closePopup: true);
        System.Threading.Interlocked.Increment(ref _completionRequestVersion);
        Dispatcher.UIThread.Post(() =>
        {
            CloseCompletionPopup();
            CloseSignatureHelpPopup();
            HoverPopup.IsOpen = false;
            LspHoverPopup.IsOpen = false;
            CodeActionPopup.IsOpen = false;
            _dismissedExceptionPopupKey = null;
        });

        _isUpdatingEditorText = true;
        try
        {
            ResetEditorViewportForDocumentSwitch();
            Editor.Document = new TextDocument(newText);
        }
        finally
        {
            _isUpdatingEditorText = false;
        }

        ApplyGrammarForPath(newPath);
        TryApplyCachedSemanticTokens(newPath);
        RestoreViewStateForActiveDocument();
        UpdateDebugRendering();
    }

    private void BeginDocumentViewStateSwitch()
    {
        _isSwitchingDocumentViewState = true;
        _viewStateTransitionVersion++;
        _pendingViewStateSavePath = null;
        _viewStateSaveTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        HideEditorDuringDocumentSwitch();
    }

    private void HideEditorDuringDocumentSwitch()
    {
        if (_isEditorHiddenForDocumentSwitch)
            return;

        _editorOpacityBeforeDocumentSwitch = Editor.Opacity;
        _isEditorHiddenForDocumentSwitch = true;
        Editor.Opacity = 0;
    }

    private void ResetEditorViewportForDocumentSwitch()
    {
        if (GetEditorScrollViewer() is { } scrollViewer)
            scrollViewer.Offset = default;
        else
        {
            Editor.ScrollToHorizontalOffset(0);
            Editor.ScrollToVerticalOffset(0);
        }
    }

    private void SaveViewStateForPath(string? path)
    {
        var viewModel = _viewModel;
        if (viewModel is null ||
            string.IsNullOrWhiteSpace(path) ||
            Editor.Document is null ||
            GetEditorScrollViewer() is not { } scrollViewer)
        {
            return;
        }

        var scrollOffset = scrollViewer.Offset;
        var state = new EditorDocumentViewState
        {
            FilePath = path,
            CaretOffset = Math.Clamp(Editor.TextArea.Caret.Offset, 0, Editor.Document.TextLength),
            ScrollX = scrollOffset.X,
            ScrollY = scrollOffset.Y,
        };

        viewModel.TaskScheduler.ScheduleLatest(
            $"editor.view-state.save.{path}",
            TaskPriority.Background,
            TimeSpan.Zero,
            ct => viewModel.SaveViewStateAsync(state, ct),
            correlationId: path);
    }

    private Task SaveCurrentViewStateAsync()
    {
        var viewModel = _viewModel;
        var path = viewModel?.ActiveDocumentPath;
        if (viewModel is null ||
            path is null ||
            Editor.Document is null ||
            _isSwitchingDocumentViewState ||
            GetEditorScrollViewer() is not { } scrollViewer)
        {
            return Task.CompletedTask;
        }

        var scrollOffset = scrollViewer.Offset;
        return viewModel.SaveViewStateAsync(new EditorDocumentViewState
        {
            FilePath = path,
            CaretOffset = Math.Clamp(Editor.TextArea.Caret.Offset, 0, Editor.Document.TextLength),
            ScrollX = scrollOffset.X,
            ScrollY = scrollOffset.Y,
        });
    }

    private void RestoreViewStateForActiveDocument()
    {
        var viewModel = _viewModel;
        var path = viewModel?.ActiveDocumentPath;
        var transitionVersion = _viewStateTransitionVersion;
        if (viewModel is null || string.IsNullOrWhiteSpace(path) || viewModel.ActiveExecutionLine is not null)
        {
            FinishDocumentViewStateSwitch(transitionVersion);
            return;
        }

        var restoreVersion = ++_viewStateRestoreVersion;
        var navigationVersion = _explicitNavigationVersion;
        viewModel.TaskScheduler.Schedule(
            "editor.view-state.restore",
            TaskPriority.Interactive,
            ct => RestoreViewStateAsync(viewModel, path, restoreVersion, navigationVersion, transitionVersion, ct),
            correlationId: transitionVersion);
    }

    private async Task RestoreViewStateAsync(
        EditorViewModel viewModel,
        string path,
        int restoreVersion,
        int navigationVersion,
        int transitionVersion,
        CancellationToken cancellationToken)
    {
        var state = await viewModel.GetViewStateAsync(path, cancellationToken);
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!CanRestoreViewState(path, restoreVersion, navigationVersion, transitionVersion) ||
                    Editor.Document is null)
                {
                    return;
                }

                if (state is not null)
                    SetCaretOffsetWithoutScrolling(Math.Clamp(state.CaretOffset, 0, Editor.Document.TextLength));
            }, DispatcherPriority.Loaded);

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            await RestoreScrollOffsetAsync(path, state, restoreVersion, navigationVersion, transitionVersion);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(
                () => FinishDocumentViewStateSwitch(transitionVersion),
                DispatcherPriority.Background);
        }
    }

    private async Task RestoreScrollOffsetAsync(
        string path,
        EditorDocumentViewState? state,
        int restoreVersion,
        int navigationVersion,
        int transitionVersion)
    {
        const int maxAttempts = 4;
        var targetX = Math.Max(0, state?.ScrollX ?? 0);
        var targetY = Math.Max(0, state?.ScrollY ?? 0);

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var shouldRetry = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!CanRestoreViewState(path, restoreVersion, navigationVersion, transitionVersion))
                {
                    return false;
                }

                if (GetEditorScrollViewer() is not { } scrollViewer)
                    return attempt + 1 < maxAttempts && (targetX > 0 || targetY > 0);

                var clampedX = Math.Clamp(targetX, 0, Math.Max(0, scrollViewer.Extent.Width - scrollViewer.Viewport.Width));
                var clampedY = Math.Clamp(targetY, 0, Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height));
                scrollViewer.Offset = new Vector(clampedX, clampedY);

                var currentOffset = scrollViewer.Offset;
                return attempt + 1 < maxAttempts &&
                       (clampedX > 0 && currentOffset.X <= 0 ||
                        clampedY > 0 && currentOffset.Y <= 0);
            }, DispatcherPriority.Render);

            if (!shouldRetry)
                return;

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        }
    }

    private bool CanRestoreViewState(
        string path,
        int restoreVersion,
        int navigationVersion,
        int transitionVersion) =>
        _viewModel is not null &&
        restoreVersion == _viewStateRestoreVersion &&
        navigationVersion == _explicitNavigationVersion &&
        transitionVersion == _viewStateTransitionVersion &&
        _viewModel.ActiveExecutionLine is null &&
        string.Equals(_viewModel.ActiveDocumentPath, path, StringComparison.OrdinalIgnoreCase);

    private void FinishDocumentViewStateSwitch(int transitionVersion)
    {
        if (transitionVersion != _viewStateTransitionVersion)
            return;

        _isSwitchingDocumentViewState = false;
        _pendingViewStateSavePath = null;
        RestoreEditorVisibilityAfterDocumentSwitch();
    }

    private void RestoreEditorVisibilityAfterDocumentSwitch()
    {
        if (!_isEditorHiddenForDocumentSwitch)
            return;

        Editor.Opacity = _editorOpacityBeforeDocumentSwitch;
        _isEditorHiddenForDocumentSwitch = false;
    }

    private void SetCaretOffsetWithoutScrolling(int offset)
    {
        Editor.TextArea.Caret.Offset = offset;
    }
}
