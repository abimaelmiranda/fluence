using System;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using Fluence.Core.Models.Debugging;

namespace Fluence.Modules.Editor.Views;

public partial class EditorView
{
    private void UpdateDebugRendering()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(UpdateDebugRendering);
            return;
        }

        var bps = _viewModel?.ActiveDocumentBreakpoints ?? Array.Empty<DebugBreakpoint>();
        _breakpointMargin?.Update(bps);
        _debugLineRenderer.Update(_viewModel?.ActiveExecutionLine);
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        UpdateDebugExceptionPopup();
    }

    private void OnBreakpointAreaPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_breakpointMargin is null) return;
        var posInMargin = e.GetPosition(_breakpointMargin);
        if (posInMargin.X < 0 || posInMargin.X > _breakpointMargin.Bounds.Width || posInMargin.Y < 0)
        {
            _breakpointMargin.SetHoveredLine(-1);
            return;
        }
        var textView = Editor.TextArea.TextView;
        if (!textView.VisualLinesValid) return;
        var vt = posInMargin.Y + textView.ScrollOffset.Y;
        var vl = textView.VisualLines.FirstOrDefault(l =>
            vt >= l.VisualTop && vt <= l.VisualTop + l.Height);
        _breakpointMargin.SetHoveredLine(vl?.FirstDocumentLine.LineNumber ?? -1);
    }

    private void OnBreakpointAreaPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null || _breakpointMargin is null) return;
        var posInMargin = e.GetPosition(_breakpointMargin);
        if (posInMargin.X < 0 || posInMargin.X > _breakpointMargin.Bounds.Width ||
            posInMargin.Y < 0)
            return;

        var textView = Editor.TextArea.TextView;
        textView.EnsureVisualLines();
        var scrollY = textView.ScrollOffset.Y;
        if (_breakpointMargin.TryToggleAtY(posInMargin.Y, scrollY, line =>
            {
                _viewModel.ToggleBreakpoint(line);
                return true;
            }))
            e.Handled = true;
    }

    private void UpdateDebugExceptionPopup()
    {
        var exception = _viewModel?.ActiveExceptionStop;
        var line = _viewModel?.ActiveExecutionLine;
        if (exception is null || line is null)
        {
            DebugExceptionPopup.IsOpen = false;
            return;
        }

        var key = $"{line.FilePath}:{line.Line}:{exception.Title}:{exception.Message}";
        if (string.Equals(key, _dismissedExceptionPopupKey, StringComparison.Ordinal))
            return;

        var placement = GetLinePopupRect(line.Line);
        if (placement is null)
        {
            DebugExceptionPopup.IsOpen = false;
            return;
        }

        DebugExceptionTitleText.Text = exception.Title;
        DebugExceptionMessageText.Text = exception.Message;
        DebugExceptionPopup.PlacementTarget = Editor.TextArea.TextView;
        DebugExceptionPopup.PlacementRect = placement.Value;
        DebugExceptionPopup.IsOpen = true;
    }

    private Rect? GetLinePopupRect(int lineNumber)
    {
        var document = Editor.Document;
        if (document is null || lineNumber < 1 || lineNumber > document.LineCount)
            return null;

        var textView = Editor.TextArea.TextView;
        textView.EnsureVisualLines();
        if (textView.GetVisualLine(lineNumber) is null)
        {
            Editor.ScrollToLine(lineNumber);
            textView.EnsureVisualLines();
            if (textView.GetVisualLine(lineNumber) is null)
                return null;
        }

        var position = new TextViewPosition(lineNumber, 1);
        var visualTop = textView.GetVisualPosition(position, VisualYPosition.LineTop);
        var scrollOffset = textView.ScrollOffset;
        return new Rect(
            Math.Max(0, visualTop.X - scrollOffset.X),
            Math.Max(0, visualTop.Y - scrollOffset.Y - 6),
            Math.Max(1, Math.Min(720, textView.Bounds.Width - 24)),
            1);
    }

    private void DismissDebugExceptionPopup()
    {
        var exception = _viewModel?.ActiveExceptionStop;
        var line = _viewModel?.ActiveExecutionLine;
        if (exception is not null && line is not null)
            _dismissedExceptionPopupKey = $"{line.FilePath}:{line.Line}:{exception.Title}:{exception.Message}";

        DebugExceptionPopup.IsOpen = false;
    }
}
