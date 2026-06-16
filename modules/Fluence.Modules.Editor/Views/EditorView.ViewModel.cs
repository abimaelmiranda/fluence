using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit.Editing;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.LanguageServer;
using Fluence.Modules.Editor.ViewModels;

namespace Fluence.Modules.Editor.Views;

public partial class EditorView
{
    private void OnLspServerReady(LspServerReadyEvent _)
    {
        var path = _viewModel?.ActiveDocumentPath;
        if (string.IsNullOrEmpty(path)) return;

        var text = Editor.Document.Text;
        _eventBus?.Publish(new DocumentOpenedEvent(path, text, "csharp"));
    }

    private void OnSemanticTokensUpdated(SemanticTokensUpdatedEvent e)
    {
        var activePath = _viewModel?.ActiveDocumentPath;
        if (!string.Equals(activePath, e.FilePath, StringComparison.OrdinalIgnoreCase))
            return;

        // Always store latest tokens so the pending redraw uses up-to-date data.
        // If a redraw is already queued, skip posting another one — it will pick up _pendingSemanticTokens.
        _pendingSemanticTokens = e.Tokens;
        if (_semanticRedrawPending) return;
        _semanticRedrawPending = true;

        Dispatcher.UIThread.Post(() =>
        {
            _semanticRedrawPending = false;
            var tokens = _pendingSemanticTokens;
            if (tokens is not null)
            {
                _semanticColorizer.Update(tokens);
                Editor.TextArea.TextView.Redraw();
            }
        }, DispatcherPriority.Background);
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
                Task.Delay(50).ContinueWith(_ => NavigateToLocation(e, retries + 1));
            return;
        }

        // LSP lines are 0-based; clamp final offset against doc length.
        var targetLine = Math.Clamp(e.Line + 1, 1, doc.LineCount);
        var docLine    = doc.GetLineByNumber(targetLine);
        var offset     = Math.Clamp(
            docLine.Offset + Math.Clamp(e.Character, 0, docLine.Length),
            0, doc.TextLength);

        SetCaretOffset(offset);
        Editor.ScrollToLine(targetLine);
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        BindViewModel();
        var lnm = Editor.TextArea.LeftMargins.OfType<LineNumberMargin>().FirstOrDefault();
        if (lnm is not null)
        {
            var idx = Editor.TextArea.LeftMargins.IndexOf(lnm);
            if (idx + 1 >= Editor.TextArea.LeftMargins.Count ||
                Editor.TextArea.LeftMargins[idx + 1] is not Border)
                Editor.TextArea.LeftMargins.Insert(idx + 1, new Border { Width = 6 });
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e) => BindViewModel();

    private void BindViewModel()
    {
        if (ReferenceEquals(_viewModel, DataContext))
        {
            if (_viewModel is not null)
                SetEditorText(_viewModel.ActiveText);
            return;
        }

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as EditorViewModel;
        if (_viewModel is not null)
        {
            _lastKnownDocumentPath = _viewModel.ActiveDocumentPath;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SetEditorText(_viewModel.ActiveText);
            ApplyGrammarForPath(_viewModel.ActiveDocumentPath);
            UpdateDebugRendering();

            // Wire LSP services if available
            if (_completionService is null && _viewModel.CompletionService is not null)
                SetServices(_viewModel.CompletionService, _viewModel.EventBus,
                    _viewModel.HoverService, _viewModel.SignatureHelpService);
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
            _lastKnownDocumentPath = newPath;
            InvalidateHoverRequests();
            System.Threading.Interlocked.Increment(ref _completionRequestVersion);
            Dispatcher.UIThread.Post(() =>
            {
                CloseCompletionPopup();
                CloseSignatureHelpPopup();
                HoverPopup.IsOpen = false;
                LspHoverPopup.IsOpen = false;
            });
            ApplyGrammarForPath(newPath);
            UpdateDebugRendering();
        }
        else if (e.PropertyName == nameof(EditorViewModel.ActiveDocumentBreakpoints) ||
                 e.PropertyName == nameof(EditorViewModel.ActiveExecutionLine))
        {
            UpdateDebugRendering();
        }
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingEditorText || _viewModel is null) return;
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
        if (_viewModel is not null)
            await _viewModel.SaveIfDirtyAsync();
    }

    private void SetEditorText(string text)
    {
        if (string.Equals(Editor.Text, text, StringComparison.Ordinal)) return;
        _isUpdatingEditorText = true;
        Editor.Text = text;
        _isUpdatingEditorText = false;
    }
}
