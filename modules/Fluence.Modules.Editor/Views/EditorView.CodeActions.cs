using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Modules.Editor.Views;

public partial class EditorView
{
    private static readonly TimeSpan CodeActionDebounceDelay = TimeSpan.FromMilliseconds(300);
    private static readonly ISolidColorBrush ActionButtonForeground = new SolidColorBrush(Color.Parse("#6EA6F0"));

    private readonly object _codeActionGate = new();
    private Timer?          _codeActionTimer;
    private LspDiagnostic?  _pendingCodeActionDiag;
    private Point           _pendingCodeActionPoint;
    private int             _codeActionVersion;
    private bool            _codeActionInFlight;

    private void ScheduleCodeActionRequest(LspDiagnostic diagnostic, Point hoverPoint)
    {
        if (_codeActionService is null) return;

        Interlocked.Increment(ref _codeActionVersion);
        lock (_codeActionGate)
        {
            _pendingCodeActionDiag  = diagnostic;
            _pendingCodeActionPoint = hoverPoint;
            _codeActionTimer ??= new Timer(
                static state => ((EditorView)state!).OnCodeActionTimerElapsed(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            _codeActionTimer.Change(CodeActionDebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnCodeActionTimerElapsed()
    {
        LspDiagnostic? diag;
        Point hoverPoint;
        lock (_codeActionGate)
        {
            if (_codeActionInFlight || _pendingCodeActionDiag is null)
                return;
            diag                   = _pendingCodeActionDiag;
            hoverPoint             = _pendingCodeActionPoint;
            _pendingCodeActionDiag = null;
            _codeActionInFlight    = true;
        }

        _ = ProcessCodeActionsAsync(diag, hoverPoint);
    }

    private async Task ProcessCodeActionsAsync(LspDiagnostic diagnostic, Point hoverPoint)
    {
        try
        {
            var filePath = _viewModel?.ActiveDocumentPath;
            if (filePath is null || _codeActionService is null)
                return;

            var version = Volatile.Read(ref _codeActionVersion);

            var actions = await _codeActionService.GetCodeActionsAsync(
                filePath,
                diagnostic.StartLine, diagnostic.StartCharacter,
                diagnostic.EndLine,   diagnostic.EndCharacter,
                diagnostic,
                CancellationToken.None).ConfigureAwait(false);

            if (Volatile.Read(ref _codeActionVersion) != version)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Volatile.Read(ref _codeActionVersion) != version)
                    return;

                DiagnosticTooltipPopup.IsOpen = false;
                ShowCodeActionPopup(diagnostic, actions, hoverPoint);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[EditorView/CodeActions] {ex.Message}"); }
        finally
        {
            lock (_codeActionGate)
            {
                _codeActionInFlight = false;
                if (_pendingCodeActionDiag is not null)
                    _codeActionTimer?.Change(CodeActionDebounceDelay, Timeout.InfiniteTimeSpan);
            }
        }
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

    private void InvalidateCodeActionRequests()
    {
        Interlocked.Increment(ref _codeActionVersion);
        lock (_codeActionGate)
        {
            _pendingCodeActionDiag = null;
        }
    }
}
