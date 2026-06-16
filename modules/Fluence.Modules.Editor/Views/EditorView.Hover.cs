using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using Fluence.Core.Models.LanguageServer;
using Fluence.Modules.Editor.ViewModels;

namespace Fluence.Modules.Editor.Views;

public partial class EditorView
{
    private void OnPointerHover(object? sender, PointerEventArgs e)
    {
        if (_viewModel is null) return;

        var textView     = Editor.TextArea.TextView;
        var textPosition = textView.GetPosition(e.GetPosition(textView) + textView.ScrollOffset);
        if (textPosition is null) return;

        var document = Editor.Document;
        if (document is null) return;

        var hoverPoint = e.GetPosition(EditorSurface);

        if (_viewModel.IsDebuggerStopped)
        {
            var word = ExtractWordAt(document, document.GetOffset(textPosition.Value.Location));
            if (string.IsNullOrWhiteSpace(word)) return;
            CancelPopupClose();
            ScheduleHoverRequest(word, hoverPoint);
            return;
        }

        var hoveredLine = textPosition.Value.Line - 1;
        var hoveredChar = textPosition.Value.Column - 1;
        var diag = _diagnosticRenderer.FindDiagnosticAt(hoveredLine, hoveredChar);
        if (diag is not null)
        {
            if (_codeActionService is not null)
            {
                CancelPopupClose();
                ScheduleCodeActionRequest(diag, hoverPoint);
            }
            else
            {
                var isError = diag.Severity == LspDiagnosticSeverity.Error;
                var prefix  = isError ? "Error" : "Warning";
                Dispatcher.UIThread.Post(() =>
                {
                    DiagnosticTooltipText.Text             = $"[{prefix}] {diag.Message}";
                    DiagnosticTooltipText.Foreground       = isError ? DiagErrorForeground   : DiagWarningForeground;
                    DiagnosticTooltipBorder.BorderBrush    = isError ? DiagErrorBorder       : DiagWarningBorder;
                    DiagnosticTooltipPopup.PlacementTarget = EditorSurface;
                    DiagnosticTooltipPopup.PlacementRect   = new Rect(hoverPoint.X + 12, hoverPoint.Y + 18, 1, 1);
                    DiagnosticTooltipPopup.IsOpen          = true;
                });
            }
            return;
        }

        if (_hoverService is not null)
            ScheduleLspHoverRequest(hoveredLine, hoveredChar, hoverPoint);
    }

    private void OnPointerHoverStopped(object? sender, PointerEventArgs e)
    {
        InvalidateHoverRequests();
        InvalidateCodeActionRequests();
        Interlocked.Increment(ref _lspHoverRequestVersion);
        lock (_lspHoverGate)
        {
            _pendingLspHoverRequest = null;
        }
        DiagnosticTooltipPopup.IsOpen = false;
        ClosePopupDelayed();
    }

    private void ScheduleHoverRequest(string expression, Point hoverPoint)
    {
        var activePath = _viewModel?.ActiveDocumentPath;
        if (string.IsNullOrWhiteSpace(activePath))
            return;

        var request = new HoverRequest(
            activePath,
            expression,
            hoverPoint,
            Interlocked.Increment(ref _hoverRequestVersion));

        lock (_hoverGate)
        {
            _pendingHoverRequest = request;
            _hoverTimer ??= new Timer(
                static state => ((EditorView)state!).OnHoverTimerElapsed(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            _hoverTimer.Change(HoverDebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnHoverTimerElapsed()
    {
        HoverRequest? request;
        lock (_hoverGate)
        {
            if (_hoverRequestInFlight || _pendingHoverRequest is null)
                return;

            request = _pendingHoverRequest;
            _pendingHoverRequest = null;
            _hoverRequestInFlight = true;
        }

        if (request is not null)
            _ = EvaluateAndShowAsync(request);
    }

    private async Task EvaluateAndShowAsync(HoverRequest request)
    {
        try
        {
            if (_viewModel is null ||
                _viewModel.ActiveDocumentPath is null ||
                !string.Equals(_viewModel.ActiveDocumentPath, request.FilePath, StringComparison.OrdinalIgnoreCase) ||
                request.Version != Volatile.Read(ref _hoverRequestVersion))
                return;

            var result = await _viewModel.EvaluateHoverAsync(request.Expression, CancellationToken.None).ConfigureAwait(false);
            if (result is null ||
                _viewModel.ActiveDocumentPath is null ||
                !string.Equals(_viewModel.ActiveDocumentPath, request.FilePath, StringComparison.OrdinalIgnoreCase) ||
                request.Version != Volatile.Read(ref _hoverRequestVersion))
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_viewModel is null ||
                    _viewModel.ActiveDocumentPath is null ||
                    !string.Equals(_viewModel.ActiveDocumentPath, request.FilePath, StringComparison.OrdinalIgnoreCase) ||
                    request.Version != Volatile.Read(ref _hoverRequestVersion))
                    return;

                var node = new HoverVariableNode(result, _viewModel.GetChildVariablesAsync);
                HoverPopup.IsOpen = false;
                HoverTree.ItemsSource = new[] { node };
                HoverPopup.PlacementTarget = EditorSurface;
                HoverPopup.PlacementRect = new Rect(request.HoverPoint.X + 12, request.HoverPoint.Y + 18, 1, 1);
                HoverPopup.IsOpen = true;
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[EditorView/Hover] {ex.Message}"); }
        finally
        {
            lock (_hoverGate)
            {
                _hoverRequestInFlight = false;
                if (_pendingHoverRequest is not null)
                {
                    _hoverTimer ??= new Timer(
                        static state => ((EditorView)state!).OnHoverTimerElapsed(),
                        this,
                        Timeout.InfiniteTimeSpan,
                        Timeout.InfiniteTimeSpan);
                    _hoverTimer.Change(HoverDebounceDelay, Timeout.InfiniteTimeSpan);
                }
            }
        }
    }

    private void InvalidateHoverRequests()
    {
        Interlocked.Increment(ref _hoverRequestVersion);
        lock (_hoverGate)
        {
            _pendingHoverRequest = null;
        }
    }

    private void ClosePopupDelayed()
    {
        lock (_popupCloseGate)
        {
            _popupCloseTimer ??= new Timer(
                static state => ((EditorView)state!).OnPopupCloseTimerElapsed(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            _popupCloseTimer.Change(PopupCloseDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void CancelPopupClose()
    {
        lock (_popupCloseGate)
        {
            _popupCloseTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnPopupCloseTimerElapsed()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_mouseInPopup)
            {
                HoverPopup.IsOpen             = false;
                LspHoverPopup.IsOpen          = false;
                DiagnosticTooltipPopup.IsOpen = false;
                CodeActionPopup.IsOpen        = false;
            }
        });
    }

    private void ScheduleLspHoverRequest(int line, int character, Point hoverPoint)
    {
        var activePath = _viewModel?.ActiveDocumentPath;
        if (string.IsNullOrWhiteSpace(activePath))
            return;

        var request = new LspHoverRequest(
            activePath, line, character, hoverPoint,
            Interlocked.Increment(ref _lspHoverRequestVersion));

        lock (_lspHoverGate)
        {
            _pendingLspHoverRequest = request;
            _lspHoverTimer ??= new Timer(
                static state => ((EditorView)state!).OnLspHoverTimerElapsed(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            _lspHoverTimer.Change(LspHoverDebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnLspHoverTimerElapsed()
    {
        LspHoverRequest? request;
        lock (_lspHoverGate)
        {
            if (_lspHoverRequestInFlight || _pendingLspHoverRequest is null)
                return;
            request = _pendingLspHoverRequest;
            _pendingLspHoverRequest = null;
            _lspHoverRequestInFlight = true;
        }

        if (request is not null)
            _ = ProcessLspHoverAsync(request);
    }

    private async Task ProcessLspHoverAsync(LspHoverRequest request)
    {
        try
        {
            if (_hoverService is null ||
                !string.Equals(_viewModel?.ActiveDocumentPath, request.FilePath, StringComparison.OrdinalIgnoreCase) ||
                request.Version != Volatile.Read(ref _lspHoverRequestVersion))
                return;

            var hover = await _hoverService.GetHoverAsync(
                request.FilePath, request.Line, request.Character, CancellationToken.None).ConfigureAwait(false);

            if (hover is null ||
                !string.Equals(_viewModel?.ActiveDocumentPath, request.FilePath, StringComparison.OrdinalIgnoreCase) ||
                request.Version != Volatile.Read(ref _lspHoverRequestVersion))
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!string.Equals(_viewModel?.ActiveDocumentPath, request.FilePath, StringComparison.OrdinalIgnoreCase) ||
                    request.Version != Volatile.Read(ref _lspHoverRequestVersion))
                    return;

                LspHoverText.Text = hover.Contents;
                LspHoverPopup.PlacementTarget = EditorSurface;
                LspHoverPopup.PlacementRect = new Rect(request.HoverPoint.X + 12, request.HoverPoint.Y + 18, 1, 1);
                LspHoverPopup.IsOpen = true;
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[EditorView/LspHover] {ex.Message}"); }
        finally
        {
            lock (_lspHoverGate)
            {
                _lspHoverRequestInFlight = false;
                if (_pendingLspHoverRequest is not null)
                    _lspHoverTimer?.Change(LspHoverDebounceDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private static string ExtractWordAt(TextDocument document, int offset)
    {
        if (offset < 0 || offset >= document.TextLength)
            return string.Empty;

        var text  = document.Text;
        var start = offset;
        while (start > 0 && IsWordChar(text[start - 1]))
            start--;

        var end = offset;
        while (end < text.Length && IsWordChar(text[end]))
            end++;

        return start < end ? text[start..end] : string.Empty;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '.';
}
