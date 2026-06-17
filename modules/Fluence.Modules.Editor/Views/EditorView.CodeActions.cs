using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Modules.Editor.Views;

public partial class EditorView
{
    private static readonly TimeSpan CodeActionDebounceDelay = TimeSpan.FromMilliseconds(300);
    private static readonly ISolidColorBrush ActionButtonForeground = new SolidColorBrush(Color.Parse("#6EA6F0"));

    private readonly object _codeActionGate = new();
    private LspDiagnostic?  _pendingCodeActionDiag;
    private Point           _pendingCodeActionPoint;
    private int             _codeActionVersion;

    private void ScheduleCodeActionRequest(LspDiagnostic diagnostic, Point hoverPoint)
    {
        if (_codeActionService is null) return;
        var filePath = _viewModel?.ActiveDocumentPath;
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
                ShowCodeActionPopup(diagnostic, actions, hoverPoint);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[EditorView/CodeActions] {ex.Message}"); }
    }

    private void ShowCodeActionPopup(LspDiagnostic diagnostic, LspCodeAction[] actions, Point hoverPoint)
    {
        var codeLabel = diagnostic.Code is not null ? $" ({diagnostic.Code})" : string.Empty;
        CodeActionDiagnosticText.Text = $"{diagnostic.Message}{codeLabel}";

        CodeActionsPanel.Children.Clear();

        if (actions.Length == 0)
        {
            CodeActionSeparator.IsVisible = false;
        }
        else
        {
            CodeActionSeparator.IsVisible = true;
            foreach (var action in actions)
            {
                var capturedAction = action;
                var label = action.IsPreferred ? $"Fix: {action.Title}" : action.Title;
                var btn = new Button
                {
                    Content         = label,
                    Foreground      = ActionButtonForeground,
                    Background      = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                    BorderThickness = new Thickness(0),
                    Padding         = new Thickness(4, 2, 4, 2),
                    Cursor          = new Cursor(StandardCursorType.Hand),
                    FontFamily      = new FontFamily("Menlo,Consolas,Cascadia Mono,monospace"),
                    FontSize        = 12,
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                };
                btn.Click += (_, _) =>
                {
                    System.Diagnostics.Debug.WriteLine($"[CodeAction] Click: {capturedAction.Title} | cmd={capturedAction.CommandIdentifier} | edit={capturedAction.Edit is not null}");
                    CodeActionPopup.IsOpen = false;
                    _ = _viewModel?.ApplyCodeActionAsync(capturedAction);
                };
                CodeActionsPanel.Children.Add(btn);
            }
        }

        CodeActionPopup.PlacementTarget = EditorSurface;
        CodeActionPopup.PlacementRect   = new Rect(hoverPoint.X + 12, hoverPoint.Y + 18, 1, 1);
        CodeActionPopup.IsOpen          = true;
    }

    private void InvalidateCodeActionRequests(bool closePopup = false)
    {
        Interlocked.Increment(ref _codeActionVersion);
        var activePath = _viewModel?.ActiveDocumentPath;
        if (!string.IsNullOrWhiteSpace(activePath))
            _viewModel?.TaskScheduler.Cancel($"editor.code-actions.{activePath}");
        lock (_codeActionGate)
        {
            _pendingCodeActionDiag = null;
        }
        if (closePopup)
            CodeActionPopup.IsOpen = false;
    }
}
