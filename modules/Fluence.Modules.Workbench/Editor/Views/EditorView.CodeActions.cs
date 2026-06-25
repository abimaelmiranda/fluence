using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit.Rendering;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Models.LanguageServer;
using Fluence.Core.Events.Lsp;

namespace Fluence.Modules.Workbench.Editor.Views;

public partial class EditorView
{
    private static readonly TimeSpan CodeActionDebounceDelay = TimeSpan.FromMilliseconds(300);

    private readonly object _codeActionGate = new();
    private LspDiagnostic?  _pendingCodeActionDiag;
    private Point           _pendingCodeActionPoint;
    private int             _codeActionVersion;

    private abstract record CodeActionItem(string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record CodeActionListItem(string Label, LspCodeAction Action) : CodeActionItem(Label);

    private sealed record FixAllListItem(string Label, LspCodeAction TemplateAction, LspDiagnostic[] AllDiagnostics) : CodeActionItem(Label);

    private void ScheduleCodeActionRequest(LspDiagnostic diagnostic, Point hoverPoint)
    {
        if (_codeActionService is null) return;
        var viewModel = _viewModel;
        var filePath = viewModel?.ActiveDocumentPath;
        if (string.IsNullOrWhiteSpace(filePath)) return;

        var version = Interlocked.Increment(ref _codeActionVersion);
        lock (_codeActionGate)
        {
            _pendingCodeActionDiag  = diagnostic;
            _pendingCodeActionPoint = hoverPoint;
        }

        _viewModel?.TaskScheduler.ScheduleLatest(
            $"editor.code-actions.{filePath}",
            TaskPriority.Background,
            CodeActionDebounceDelay,
            ct => ProcessCodeActionsAsync(filePath, diagnostic, hoverPoint, version, ct),
            correlationId: version);
    }

    private void TriggerQuickFix()
    {
        if (CodeActionPopup.IsOpen)
        {
            CodeActionPopup.IsOpen = false;
            return;
        }

        if (_codeActionService is null) return;
        var viewModel = _viewModel;
        var filePath = viewModel?.ActiveDocumentPath;
        if (string.IsNullOrWhiteSpace(filePath)) return;

        var caret = Editor.TextArea.Caret;
        var line = caret.Line - 1;
        var character = caret.Column - 1;
        var diagnostic = _diagnosticRenderer.FindDiagnosticForLine(line, character);
        var version = Interlocked.Increment(ref _codeActionVersion);

        DiagnosticTooltipPopup.IsOpen = false;
        LspHoverPopup.IsOpen = false;
        HoverPopup.IsOpen = false;
        CodeActionPopup.IsOpen = false;
        viewModel.EventBus.Publish(new LspInteractiveRequestStartedEvent(filePath));
        viewModel.PublishLiveDocumentChanged(Editor.Text, flushImmediately: true);

        viewModel.TaskScheduler.ScheduleLatest(
            $"editor.quick-fix.{filePath}",
            TaskPriority.Input,
            TimeSpan.Zero,
            ct => ProcessQuickFixAsync(filePath, line, character, diagnostic, version, ct),
            correlationId: version);
    }

    private async Task ProcessCodeActionsAsync(
        string filePath,
        LspDiagnostic diagnostic,
        Point hoverPoint,
        int version,
        CancellationToken ct)
    {
        try
        {
            if (_codeActionService is null ||
                !string.Equals(_viewModel?.ActiveDocumentPath, filePath, StringComparison.OrdinalIgnoreCase) ||
                Volatile.Read(ref _codeActionVersion) != version ||
                ct.IsCancellationRequested)
                return;

            var actions = await _codeActionService.GetCodeActionsAsync(
                filePath,
                diagnostic.StartLine, diagnostic.StartCharacter,
                diagnostic.EndLine,   diagnostic.EndCharacter,
                diagnostic,
                ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested ||
                Volatile.Read(ref _codeActionVersion) != version ||
                !string.Equals(_viewModel?.ActiveDocumentPath, filePath, StringComparison.OrdinalIgnoreCase))
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested ||
                    Volatile.Read(ref _codeActionVersion) != version ||
                    !string.Equals(_viewModel?.ActiveDocumentPath, filePath, StringComparison.OrdinalIgnoreCase))
                    return;

                DiagnosticTooltipPopup.IsOpen = false;
                ShowCodeActionPopup(diagnostic.Message, actions, hoverPoint, placementTarget: EditorSurface, diagnostic: diagnostic);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[EditorView/CodeActions] {ex.Message}"); }
    }

    private async Task ProcessQuickFixAsync(
        string filePath,
        int line,
        int character,
        LspDiagnostic? diagnostic,
        int version,
        CancellationToken ct)
    {
        try
        {
            if (_codeActionService is null ||
                !string.Equals(_viewModel?.ActiveDocumentPath, filePath, StringComparison.OrdinalIgnoreCase) ||
                Volatile.Read(ref _codeActionVersion) != version ||
                ct.IsCancellationRequested)
                return;

            var startLine = diagnostic?.StartLine ?? line;
            var startChar = diagnostic?.StartCharacter ?? character;
            var endLine = diagnostic?.EndLine ?? line;
            var endChar = diagnostic?.EndCharacter ?? character;
            var actions = await _codeActionService.GetCodeActionsAsync(
                filePath,
                startLine, startChar,
                endLine, endChar,
                diagnostic,
                ct).ConfigureAwait(false);

            if (ct.IsCancellationRequested ||
                Volatile.Read(ref _codeActionVersion) != version ||
                !string.Equals(_viewModel?.ActiveDocumentPath, filePath, StringComparison.OrdinalIgnoreCase))
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested ||
                    Volatile.Read(ref _codeActionVersion) != version ||
                    !string.Equals(_viewModel?.ActiveDocumentPath, filePath, StringComparison.OrdinalIgnoreCase))
                    return;

                var title = diagnostic is not null
                    ? FormatDiagnosticTitle(diagnostic)
                    : _viewModel?.Localization.Get("Editor.CodeAction.QuickFix") ?? "Quick Fix";
                ShowCodeActionPopup(title, actions, GetCaretPopupRect(), placementTarget: Editor.TextArea.TextView, diagnostic: diagnostic);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[EditorView/QuickFix] {ex.Message}"); }
    }

    private void ShowCodeActionPopup(string title, LspCodeAction[] actions, Point point, Control placementTarget,
        LspDiagnostic? diagnostic = null)
    {
        ShowCodeActionPopup(title, actions, new Rect(point.X + 12, point.Y + 18, 1, 1), placementTarget, diagnostic);
    }

    private void ShowCodeActionPopup(string title, LspCodeAction[] actions, Rect placementRect, Control placementTarget,
        LspDiagnostic? diagnostic = null)
    {
        CodeActionDiagnosticText.Text = title;

        CodeActionsListBox.ItemsSource = null;
        if (actions.Length == 0)
        {
            CodeActionSeparator.IsVisible = false;
            CodeActionEmptyText.IsVisible = true;
            CodeActionsListBox.IsVisible = false;
        }
        else
        {
            CodeActionSeparator.IsVisible = true;
            CodeActionEmptyText.IsVisible = false;
            CodeActionsListBox.IsVisible = true;

            var items = new List<CodeActionItem>(actions.Length + 2);
            foreach (var action in actions)
            {
                items.Add(new CodeActionListItem(
                    action.IsPreferred
                        ? string.Format(_viewModel?.Localization.Get("Editor.CodeAction.FixPrefix") ?? "Fix: {0}", action.Title)
                        : action.Title,
                    action));
            }

            if (diagnostic?.Code is { } code)
            {
                var allWithCode = _diagnosticRenderer.GetAll()
                    .Where(d => d.Code == code)
                    .ToArray();
                if (allWithCode.Length > 1)
                {
                    foreach (var action in actions.Where(a => a.IsPreferred))
                    {
                        var fixAllLabel = string.Format(
                            _viewModel?.Localization.Get("Editor.CodeAction.FixAllPrefix") ?? "Fix All: {0}",
                            action.Title);
                        items.Add(new FixAllListItem(fixAllLabel, action, allWithCode));
                    }
                }
            }

            CodeActionsListBox.ItemsSource = items.ToArray();
            CodeActionsListBox.SelectedIndex = 0;
        }

        CodeActionPopup.PlacementTarget = placementTarget;
        CodeActionPopup.PlacementRect   = placementRect;
        CodeActionPopup.IsOpen          = true;
    }

    private void ApplySelectedCodeAction()
    {
        if (CodeActionsListBox.SelectedItem is not CodeActionItem item)
            return;

        CodeActionPopup.IsOpen = false;
        switch (item)
        {
            case CodeActionListItem single:
                System.Diagnostics.Debug.WriteLine($"[CodeAction] Apply: {single.Action.Title} | cmd={single.Action.CommandIdentifier} | edit={single.Action.Edit is not null}");
                _ = _viewModel?.ApplyCodeActionAsync(single.Action);
                break;
            case FixAllListItem fixAll:
                System.Diagnostics.Debug.WriteLine($"[CodeAction] Fix All: {fixAll.TemplateAction.Title} | count={fixAll.AllDiagnostics.Length}");
                _ = _viewModel?.ApplyFixAllAsync(fixAll.TemplateAction, fixAll.AllDiagnostics);
                break;
        }
    }

    private void OnCodeActionListBoxDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        ApplySelectedCodeAction();
        e.Handled = true;
    }

    private bool TryHandleCodeActionPopupKey(KeyEventArgs e)
    {
        if (!CodeActionPopup.IsOpen)
            return false;

        if (e.Key == Key.Escape)
        {
            CodeActionPopup.IsOpen = false;
            return true;
        }

        if (CodeActionsListBox.ItemCount == 0)
            return e.Key is Key.Up or Key.Down or Key.Enter;

        if (e.Key == Key.Down)
        {
            CodeActionsListBox.SelectedIndex = Math.Min(CodeActionsListBox.SelectedIndex + 1, CodeActionsListBox.ItemCount - 1);
            return true;
        }

        if (e.Key == Key.Up)
        {
            CodeActionsListBox.SelectedIndex = Math.Max(CodeActionsListBox.SelectedIndex - 1, 0);
            return true;
        }

        if (e.Key == Key.Enter)
        {
            ApplySelectedCodeAction();
            return true;
        }

        return false;
    }

    private Rect GetCaretPopupRect()
    {
        var textView = Editor.TextArea.TextView;
        textView.EnsureVisualLines();
        var caretPos = Editor.TextArea.Caret.Position;
        if (textView.GetVisualLine(caretPos.Line) is null)
            return new Rect(0, 0, 1, 1);

        var visualBottom = textView.GetVisualPosition(caretPos, VisualYPosition.LineBottom);
        var scrollOffset = textView.ScrollOffset;
        return new Rect(visualBottom.X - scrollOffset.X, visualBottom.Y - scrollOffset.Y, 1, 1);
    }

    private static string FormatDiagnosticTitle(LspDiagnostic diagnostic)
    {
        var codeLabel = diagnostic.Code is not null ? $" ({diagnostic.Code})" : string.Empty;
        return $"{diagnostic.Message}{codeLabel}";
    }

    private void InvalidateCodeActionRequests(bool closePopup = false)
    {
        Interlocked.Increment(ref _codeActionVersion);
        var activePath = _viewModel?.ActiveDocumentPath;
        if (!string.IsNullOrWhiteSpace(activePath))
        {
            _viewModel?.TaskScheduler.Cancel($"editor.code-actions.{activePath}");
            _viewModel?.TaskScheduler.Cancel($"editor.quick-fix.{activePath}");
        }
        lock (_codeActionGate)
        {
            _pendingCodeActionDiag = null;
        }
        if (closePopup)
        {
            CodeActionsListBox.ItemsSource = null;
            CodeActionPopup.IsOpen = false;
        }
    }
}
