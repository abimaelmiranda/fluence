using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Fluence.Core.Models.Debugging;

namespace Fluence.Modules.Editor.Rendering;

internal sealed class BreakpointMargin : AbstractMargin
{
    private static readonly IBrush GhostBrush = Brushes.DarkRed;

    private readonly Action<int> _toggleBreakpoint;
    private IReadOnlyList<DebugBreakpoint> _breakpoints = [];
    private Dictionary<int, DebugBreakpoint> _breakpointsByLine = [];
    private int _hoveredLine = -1;

    public BreakpointMargin(Action<int> toggleBreakpoint)
    {
        _toggleBreakpoint = toggleBreakpoint;
        Width  = 22;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    public void Update(IReadOnlyList<DebugBreakpoint> breakpoints)
    {
        _breakpoints = breakpoints;
        _breakpointsByLine = new Dictionary<int, DebugBreakpoint>(breakpoints.Count);
        foreach (var bp in breakpoints)
            _breakpointsByLine[bp.Line] = bp;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (TextView is null || !TextView.VisualLinesValid) return;

        foreach (var vl in TextView.VisualLines)
        {
            var lineNumber = vl.FirstDocumentLine.LineNumber;
            var y  = vl.VisualTop - TextView.ScrollOffset.Y + vl.Height / 2;
            _breakpointsByLine.TryGetValue(lineNumber, out var bp);

            if (bp is not null)
            {
                var brush = bp.IsVerified ? Brushes.IndianRed : Brushes.DarkRed;
                context.DrawEllipse(brush, null, new Point(11, y), 6, 6);
            }
            else if (lineNumber == _hoveredLine)
            {
                context.DrawEllipse(GhostBrush, null, new Point(11, y), 6, 6);
            }
        }
    }

    public void SetHoveredLine(int lineNumber)
    {
        if (_hoveredLine == lineNumber) return;
        _hoveredLine = lineNumber;
        InvalidateVisual();
        if (lineNumber == -1) return;
        var hasBreakpoint = _breakpointsByLine.ContainsKey(lineNumber);
        ToolTip.SetTip(this, hasBreakpoint ? "Click to remove breakpoint" : "Click to add a breakpoint");
    }

    public bool TryToggleAtY(double y, double scrollOffsetY, Func<int, bool> toggleBreakpoint)
    {
        if (TextView is null || !TextView.VisualLinesValid) return false;
        var vt = y + scrollOffsetY;
        var vl = TextView.VisualLines.FirstOrDefault(l =>
            vt >= l.VisualTop && vt <= l.VisualTop + l.Height);
        if (vl is null) return false;
        toggleBreakpoint(vl.FirstDocumentLine.LineNumber);
        return true;
    }
}
