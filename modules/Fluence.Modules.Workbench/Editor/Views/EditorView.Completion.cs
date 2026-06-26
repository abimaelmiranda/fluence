using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Models.LanguageServer;
using Fluence.Modules.Workbench.Editor.Completion;
using Fluence.Core.Events.Lsp;

namespace Fluence.Modules.Workbench.Editor.Views;

public partial class EditorView
{
    private Task TriggerCompletionAsync(bool immediate = false)
    {
        if (_completionService is null || _viewModel?.ActiveDocumentPath is null)
            return Task.CompletedTask;

        var caret = Editor.TextArea.Caret;
        var request = new CompletionRequest(
            _viewModel.ActiveDocumentPath,
            caret.Line - 1,
            caret.Column - 1,
            caret.Offset,
            Interlocked.Increment(ref _completionRequestVersion));

        _viewModel.EventBus.Publish(new LspInteractiveRequestStartedEvent(request.FilePath));
        _viewModel.PublishLiveDocumentChanged(Editor.Text, flushImmediately: true);
        ScheduleCompletionRequest(request, immediate ? DotCompletionDelay : CompletionDebounce);
        return Task.CompletedTask;
    }

    private void ScheduleCompletionRequest(CompletionRequest request, TimeSpan delay)
    {
        lock (_completionGate)
        {
            _pendingCompletionRequest = request;
            _completionTimer ??= new Timer(
                static state => ((EditorView)state!).OnCompletionTimerElapsed(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            _completionTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnCompletionTimerElapsed()
    {
        CompletionRequest? request;
        lock (_completionGate)
        {
            if (_completionRequestInFlight || _pendingCompletionRequest is null)
                return;

            request = _pendingCompletionRequest;
            _pendingCompletionRequest = null;
            _completionRequestInFlight = true;
        }

        if (request is not null)
        {
            var scheduler = _viewModel?.TaskScheduler;
            if (scheduler is null)
            {
                FinishCompletionRequest();
                return;
            }

            scheduler.Schedule(
                $"editor.completion.{request.FilePath}",
                TaskPriority.Input,
                ct => ProcessCompletionRequestAsync(request, ct),
                correlationId: request.Version);
        }
    }

    private async Task ProcessCompletionRequestAsync(CompletionRequest request, CancellationToken ct)
    {
        try
        {
            if (_completionService is null ||
                _viewModel?.ActiveDocumentPath is null ||
                !string.Equals(_viewModel.ActiveDocumentPath, request.FilePath, StringComparison.OrdinalIgnoreCase) ||
                request.Version != Volatile.Read(ref _completionRequestVersion) ||
                ct.IsCancellationRequested)
                return;

            var completions = await _completionService.GetCompletionsAsync(
                request.FilePath,
                request.Line,
                request.Character,
                ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested ||
                request.Version != Volatile.Read(ref _completionRequestVersion) ||
                completions.Count == 0)
                return;

            var sortedCompletions = PrepareCompletionItems(completions, ct);
            if (ct.IsCancellationRequested ||
                request.Version != Volatile.Read(ref _completionRequestVersion) ||
                sortedCompletions.Count == 0)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_completionService is null ||
                    _viewModel?.ActiveDocumentPath is null ||
                    !string.Equals(_viewModel.ActiveDocumentPath, request.FilePath, StringComparison.OrdinalIgnoreCase) ||
                    request.Version != Volatile.Read(ref _completionRequestVersion) ||
                    ct.IsCancellationRequested ||
                    Editor.TextArea.Caret.Offset != request.CaretOffset)
                    return;

                CloseCompletionPopup();
                _activeCompletions = new List<LspCompletionData>(sortedCompletions.Count);
                for (var i = 0; i < sortedCompletions.Count; i++)
                {
                    _activeCompletions.Add(new LspCompletionData(
                        sortedCompletions[i],
                        100000 - i,
                        _semanticColorizer,
                        _editorSettings.FontFamily));
                }
                var caretOffsetNow = Editor.TextArea.Caret.Offset;
                var currentPrefix = ExtractCompletionPrefix(Editor.Document, caretOffsetNow);
                _completionTriggerOffset = caretOffsetNow - currentPrefix.Length;
                Editor.TextArea.Caret.PositionChanged -= OnCaretPositionChangedForCompletion;
                Editor.TextArea.Caret.PositionChanged += OnCaretPositionChangedForCompletion;
                RefreshCompletionWindowItems();
                ShowCompletionPopup();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[EditorView/Completion] {ex.Message}"); }
        finally
        {
            FinishCompletionRequest();
        }
    }

    private static List<LspCompletion> PrepareCompletionItems(IReadOnlyList<LspCompletion> completions, CancellationToken ct)
    {
        var sorted = new List<LspCompletion>(completions.Count);
        for (var i = 0; i < completions.Count; i++)
        {
            if (ct.IsCancellationRequested)
                return [];

            var item = completions[i];
            if (item is not null && !string.IsNullOrEmpty(item.Label))
                sorted.Add(item);
        }

        sorted.Sort(CompareCompletionItems);
        return sorted;
    }

    private static int CompareCompletionItems(LspCompletion? left, LspCompletion? right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left is null)
            return 1;
        if (right is null)
            return -1;

        var sortTextResult = (left.SortText is null ? 1 : 0).CompareTo(right.SortText is null ? 1 : 0);
        if (sortTextResult != 0)
            return sortTextResult;

        sortTextResult = string.Compare(left.SortText, right.SortText, StringComparison.Ordinal);
        if (sortTextResult != 0)
            return sortTextResult;

        return (left.IsPreselected ? 0 : 1).CompareTo(right.IsPreselected ? 0 : 1);
    }

    private void FinishCompletionRequest()
    {
        lock (_completionGate)
        {
            _completionRequestInFlight = false;
            if (_pendingCompletionRequest is not null)
            {
                _completionTimer ??= new Timer(
                    static state => ((EditorView)state!).OnCompletionTimerElapsed(),
                    this,
                    Timeout.InfiniteTimeSpan,
                    Timeout.InfiniteTimeSpan);
                _completionTimer.Change(CompletionDebounce, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void ScheduleCompletionWindowRefresh()
    {
        if (!CompletionPopup.IsOpen || _activeCompletions is null)
            return;

        _completionRefreshPending = true;
        _completionRefreshTimer?.Stop();
        _completionRefreshTimer?.Start();
    }

    private void OnCompletionRefreshTimerTick(object? sender, EventArgs e)
    {
        _completionRefreshTimer?.Stop();
        if (!_completionRefreshPending)
            return;

        _completionRefreshPending = false;
        RefreshCompletionWindowItems();
    }

    private void OnCaretPositionChangedForCompletion(object? sender, EventArgs e)
    {
        if (!CompletionPopup.IsOpen || _completionTriggerOffset < 0)
            return;
        if (Editor.TextArea.Caret.Offset < _completionTriggerOffset)
        {
            // Defer close by one UI cycle to avoid false positives from transient caret
            // repositioning that AvaloniaEdit may emit during visual layout (e.g. EnsureVisualLines).
            Dispatcher.UIThread.Post(() =>
            {
                if (CompletionPopup.IsOpen &&
                    _completionTriggerOffset >= 0 &&
                    Editor.TextArea.Caret.Offset < _completionTriggerOffset)
                    CloseCompletionPopup();
            }, DispatcherPriority.Background);
        }
    }

    private void ShowCompletionPopup()
    {
        var textView = Editor.TextArea.TextView;
        textView.EnsureVisualLines();
        var caretPos = Editor.TextArea.Caret.Position;
        // Guard: the caret line might not have a visual line if it's outside the rendered viewport.
        if (textView.GetVisualLine(caretPos.Line) is null)
            return;
        var visualBottom = textView.GetVisualPosition(caretPos, VisualYPosition.LineBottom);
        var scrollOffset = textView.ScrollOffset;
        // Use textView as PlacementTarget (same pattern as CompletionWindowBase in AvaloniaEdit).
        // PlacementRect is in textView's viewport coordinate space: document position minus scroll offset.
        CompletionPopup.PlacementTarget = textView;
        CompletionPopup.PlacementRect = new Rect(
            visualBottom.X - scrollOffset.X,
            visualBottom.Y - scrollOffset.Y,
            1, 1);
        CompletionPopup.IsOpen = true;
    }

    private void CloseCompletionPopup()
    {
        if (!CompletionPopup.IsOpen) return;
        Editor.TextArea.Caret.PositionChanged -= OnCaretPositionChangedForCompletion;
        CompletionPopup.IsOpen = false;
        _activeCompletions = null;
        _completionTriggerOffset = -1;
        CompletionListBox.ItemsSource = null;
    }

    private void CommitCompletion()
    {
        if (!CompletionPopup.IsOpen || _activeCompletions is null) return;
        var selected = CompletionListBox.SelectedItem as LspCompletionData;
        CloseCompletionPopup();
        if (selected is not null)
        {
            var segment = new AnchorSegment(Editor.Document, Editor.TextArea.Caret.Offset, 0);
            selected.Complete(Editor.TextArea, segment, EventArgs.Empty);
            // TODO: auto-add missing using directive after completion commit.
            // OmniSharp does not return additionalTextEdits via completionItem/resolve;
            // a diagnostic-driven approach was attempted but reverted — needs a better strategy.
        }
    }

    private void RefreshCompletionWindowItems()
    {
        if (_activeCompletions is null)
            return;

        var prefix = ExtractCompletionPrefix(Editor.Document, Editor.TextArea.Caret.Offset);
        var filtered = _activeCompletions;
        if (!string.IsNullOrWhiteSpace(prefix))
        {
            _filteredCompletions.Clear();
            _filteredCompletions.Capacity = Math.Max(_filteredCompletions.Capacity, _activeCompletions.Count);
            for (var i = 0; i < _activeCompletions.Count; i++)
            {
                var item = _activeCompletions[i];
                if (item.MatchesPrefix(prefix))
                    _filteredCompletions.Add(item);
            }
            filtered = _filteredCompletions;
        }

        if (filtered.Count == 0)
        {
            CloseCompletionPopup();
            return;
        }

        CompletionListBox.ItemsSource = null;
        CompletionListBox.ItemsSource = filtered;
        CompletionListBox.SelectedIndex = 0;
    }

    private static string ExtractCompletionPrefix(TextDocument? document, int offset)
    {
        if (document is null || offset <= 0 || document.TextLength == 0)
            return string.Empty;

        var end   = Math.Clamp(offset, 0, document.TextLength);
        var index = end;

        while (index > 0 && IsCompletionChar(document.GetCharAt(index - 1)))
            index--;

        return index < end ? document.GetText(index, end - index) : string.Empty;
    }

    private static bool IsCompletionChar(char ch) => char.IsLetterOrDigit(ch) || ch == '_';
}
