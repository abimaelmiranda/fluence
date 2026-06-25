using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit.Editing;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.LanguageServer;
using Fluence.Core.Services;
using Fluence.Modules.Workbench.Editor.ViewModels;
using Fluence.Core.Events.Document;
using Fluence.Core.Events.Lsp;
using Fluence.Core.Models.Keybindings;

namespace Fluence.Modules.Workbench.Editor.Views;

public partial class EditorView
{
    private void OnSemanticTokensUpdated(SemanticTokensUpdatedEvent e)
    {
        _semanticTokensByPath[e.FilePath] = new CachedSemanticTokens(e.Version, e.Tokens);

        // During a document switch, only update the cache — TryApplyCachedSemanticTokens
        // at the end of SwitchEditorDocument will apply the correct tokens for the new document.
        if (_isSwitchingDocumentViewState)
            return;

        var activeVersion = _viewModel?.ActiveDocumentVersion ?? 0;
        if (!IsActiveSemanticTokenEvent(e, activeVersion))
            return;

        // Always store latest tokens so the pending redraw uses up-to-date data.
        // Atomic gate: only one Post in flight at a time — the queued Post reads the latest pending tokens.
        _pendingSemanticTokens = e.Tokens;
        _pendingSemanticTokensPath = e.FilePath;
        _pendingSemanticTokensVersion = e.Version;
        if (Interlocked.CompareExchange(ref _semanticRedrawPending, 1, 0) != 0)
            return;

        var viewModel = _viewModel;
        if (viewModel is null) return;

        viewModel.Dispatcher.Post(() =>
        {
            Interlocked.Exchange(ref _semanticRedrawPending, 0);
            if (_isSwitchingDocumentViewState) return;
            var tokens = _pendingSemanticTokens;
            var path = _pendingSemanticTokensPath;
            var version = _pendingSemanticTokensVersion;
            if (tokens is not null && IsActiveSemanticTokenState(path, version))
            {
                _semanticColorizer.Update(tokens);
                _pendingSemanticTokens = null;
                _pendingSemanticTokensPath = null;
                _pendingSemanticTokensVersion = 0;
                Editor.TextArea.TextView.Redraw();
            }
        });
    }

    private void OnDocumentClosed(DocumentClosedEvent e)
    {
        _semanticTokensByPath.Remove(e.FilePath);
    }

    private bool IsActiveSemanticTokenEvent(SemanticTokensUpdatedEvent e, int version)
    {
        var viewModel = _viewModel;
        return viewModel is not null &&
               version == e.Version &&
               viewModel.ActiveDocumentVersion == e.Version &&
               string.Equals(viewModel.ActiveDocumentPath, e.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsActiveSemanticTokenState(string? path, int version)
    {
        var viewModel = _viewModel;
        return viewModel is not null &&
               version > 0 &&
               viewModel.ActiveDocumentVersion == version &&
               string.Equals(viewModel.ActiveDocumentPath, path, StringComparison.OrdinalIgnoreCase);
    }

    private void TryApplyCachedSemanticTokens(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            _viewModel is null ||
            !_semanticTokensByPath.TryGetValue(path, out var cached) ||
            cached.Version != _viewModel.ActiveDocumentVersion)
        {
            return;
        }

        _semanticColorizer.Update(cached.Tokens);
        Editor.TextArea.TextView.Redraw();
    }

    private void OnNavigationResolved(NavigationResolvedEvent e) => NavigateToLocation(e, retries: 0);

    private void NavigateToLocation(NavigationResolvedEvent e, int retries)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => NavigateToLocation(e, retries), DispatcherPriority.Background);
            return;
        }

        var doc = Editor.Document;
        if (doc is null) return;

        // Cross-file navigation: wait for the correct document to be loaded.
        var loadedPath = _viewModel?.ActiveDocumentPath;
        if (!string.Equals(loadedPath, e.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            if (retries < 10)
                ScheduleNavigationRetry(e, retries + 1);
            return;
        }

        // LSP lines are 0-based; clamp final offset against doc length.
        var targetLine = Math.Clamp(e.Line + 1, 1, doc.LineCount);
        var docLine    = doc.GetLineByNumber(targetLine);
        var offset     = Math.Clamp(
            docLine.Offset + Math.Clamp(e.Character, 0, docLine.Length),
            0, doc.TextLength);

        _explicitNavigationVersion++;
        SetCaretOffset(offset);
        Editor.ScrollToLine(targetLine);
        SaveViewStateForPath(e.FilePath);
    }

    private void ScheduleNavigationRetry(NavigationResolvedEvent e, int retries)
    {
        var viewModel = _viewModel;
        if (viewModel is null)
            return;

        viewModel.TaskScheduler.ScheduleLatest(
            "editor.navigation.retry",
            Fluence.Core.Abstractions.Tasks.TaskPriority.Interactive,
            TimeSpan.FromMilliseconds(50),
            _ =>
            {
                Dispatcher.UIThread.Post(() => NavigateToLocation(e, retries), DispatcherPriority.Background);
                return Task.CompletedTask;
            },
            correlationId: (e.FilePath, e.Line, e.Character));
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        EnsureEditorTimers();
        BindViewModel();
        EnsureEditorScrollViewerSubscription();
        Dispatcher.UIThread.Post(EnsureEditorScrollViewerSubscription, DispatcherPriority.Loaded);
        var lnm = Editor.TextArea.LeftMargins.OfType<LineNumberMargin>().FirstOrDefault();
        if (lnm is not null)
        {
            var idx = Editor.TextArea.LeftMargins.IndexOf(lnm);
            if (idx + 1 >= Editor.TextArea.LeftMargins.Count ||
                Editor.TextArea.LeftMargins[idx + 1] is not Border)
                Editor.TextArea.LeftMargins.Insert(idx + 1, new Border { Width = 6 });
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) =>
        ReleaseEditorRuntimeSubscriptions();

    private void OnDataContextChanged(object? sender, EventArgs e) => BindViewModel();

    private void BindViewModel()
    {
        if (ReferenceEquals(_viewModel, DataContext))
        {
            if (_viewModel is not null)
            {
                if (string.Equals(_viewModel.ActiveDocumentPath, _lastKnownDocumentPath, StringComparison.OrdinalIgnoreCase))
                    SetEditorText(_viewModel.ActiveText);
                else
                    SwitchEditorDocument(_viewModel.ActiveDocumentPath, _viewModel.ActiveText);
            }
            return;
        }

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _editorSettingsSubscription?.Dispose();
            _editorSettingsSubscription = null;
            _keybindingsSubscription?.Dispose();
            _keybindingsSubscription = null;
            _themeSubscription?.Dispose();
            _themeSubscription = null;
        }

        _viewModel = DataContext as EditorViewModel;
        if (_viewModel is not null)
        {
            _lastKnownDocumentPath = _viewModel.ActiveDocumentPath;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _editorSettingsSubscription = _viewModel.Settings
                .Watch<EditorSettings>()
                .Subscribe(new ActionObserver<EditorSettings>(ApplyEditorSettings));
            _keybindingsSubscription = _viewModel.Keybindings.Watch()
                .Subscribe(new ActionObserver<IReadOnlyList<KeybindingDefinition>>(_ => Dispatcher.UIThread.Post(UpdateMenuGestures)));
            if (_viewModel.ThemeLoader is not null)
            {
                _themeSubscription = _viewModel.ThemeLoader
                    .Watch()
                    .Subscribe(new ActionObserver<Fluence.Core.Models.Theming.IdeTheme>(ApplyTheme));
                ApplyTheme(_viewModel.ThemeLoader.CurrentTheme);
            }
            ApplyEditorSettings(_viewModel.Settings.Get<EditorSettings>());
            UpdateMenuGestures();
            SetEditorText(_viewModel.ActiveText);
            ApplyGrammarForPath(_viewModel.ActiveDocumentPath);
            RestoreViewStateForActiveDocument();
            UpdateDebugRendering();

            // Wire LSP services if available
            if (_completionService is null && _viewModel.CompletionService is not null)
                SetServices(_viewModel.CompletionService, _viewModel.EventBus,
                    _viewModel.HoverService, _viewModel.SignatureHelpService, _viewModel.CodeActionService);
        }
        else
        {
            SetEditorText(string.Empty);
            UpdateDebugRendering();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_viewModel is null) return;

        if (e.PropertyName == nameof(EditorViewModel.ActiveText))
        {
            var activePath = _viewModel.ActiveDocumentPath;
            if (!string.Equals(activePath, _lastKnownDocumentPath, StringComparison.OrdinalIgnoreCase))
            {
                BeginDocumentViewStateSwitch();
                SaveViewStateForPath(_lastKnownDocumentPath);
                _savedViewStateDuringTextSwitchPath = _lastKnownDocumentPath;
                return;
            }

            SetEditorText(_viewModel.ActiveText);
        }
        else if (e.PropertyName == nameof(EditorViewModel.ActiveDocumentPath))
        {
            var newPath = _viewModel.ActiveDocumentPath;
            if (string.Equals(newPath, _lastKnownDocumentPath, StringComparison.OrdinalIgnoreCase))
            {
                // Path unchanged — RefreshFromWorkspace fires this after every save.
                // Skip popup close and completion invalidation to avoid killing active completion.
                ApplyGrammarForPath(newPath);
                UpdateDebugRendering();
                return;
            }

            SwitchEditorDocument(newPath, _viewModel.ActiveText);
        }
        else if (e.PropertyName == nameof(EditorViewModel.ActiveDocumentBreakpoints) ||
                 e.PropertyName == nameof(EditorViewModel.ActiveExecutionLine) ||
                 e.PropertyName == nameof(EditorViewModel.ActiveExceptionStop))
        {
            if (e.PropertyName == nameof(EditorViewModel.ActiveExecutionLine))
                _dismissedExceptionPopupKey = null;

            UpdateDebugRendering();
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingEditorText || _viewModel is null) return;
        InvalidateLspHoverRequests();
        InvalidateCodeActionRequests(closePopup: true);
        _viewModel.PublishLiveDocumentChanged(Editor.Text, flushImmediately: false);
        _textSyncTimer!.Change(TextSyncDebounceDelay, Timeout.InfiniteTimeSpan);
    }

    private void OnTextSyncTimerElapsed()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_viewModel is null || _isUpdatingEditorText) return;
            _viewModel.ActiveText = Editor.Text;
        }, DispatcherPriority.Background);
    }

    private async void OnEditorLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var viewModel = _viewModel;
        if (viewModel is null) return;

        try
        {
            await SaveCurrentViewStateAsync();
            await viewModel.SaveIfDirtyAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EditorView] OnEditorLostFocus failed: {ex.Message}");
        }
    }

    private void SetEditorText(string text)
    {
        if (string.Equals(Editor.Text, text, StringComparison.Ordinal)) return;
        _isUpdatingEditorText = true;
        try
        {
            Editor.Text = text;
        }
        finally
        {
            _isUpdatingEditorText = false;
        }
    }

}
