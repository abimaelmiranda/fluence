using System;
using System.Linq;
using Avalonia.Input;
using AvaloniaEdit.Rendering;
using Fluence.Core.Models.Debugging;

namespace Fluence.Modules.Editor.Views;

public partial class EditorView
{
    private void UpdateDebugRendering()
    {
        var bps = _viewModel?.ActiveDocumentBreakpoints ?? Array.Empty<DebugBreakpoint>();
        _breakpointMargin?.Update(bps);
        _debugLineRenderer.Update(_viewModel?.ActiveExecutionLine);
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
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
}
