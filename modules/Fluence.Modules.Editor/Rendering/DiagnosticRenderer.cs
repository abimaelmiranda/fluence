using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using AvaloniaEdit;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Modules.Editor.Rendering;

internal sealed class DiagnosticRenderer : IBackgroundRenderer
{
    private static readonly ISolidColorBrush ErrorBrush   = new SolidColorBrush(Color.FromRgb(255, 80, 80));
    private static readonly ISolidColorBrush WarningBrush = new SolidColorBrush(Color.FromRgb(220, 180, 80));
    private static readonly DashStyle        WiggleDash   = new DashStyle([2, 2], 0);
    private static readonly IPen             ErrorPen     = new Pen(ErrorBrush,   1.5, WiggleDash);
    private static readonly IPen             WarningPen   = new Pen(WarningBrush, 1.5, WiggleDash);

    private IReadOnlyList<LspDiagnostic> _diagnostics = [];
    private TextDocument? _document;
    private readonly Dictionary<int, VisualLine> _visualLineMap = [];

    public KnownLayer Layer => KnownLayer.Selection;

    public void Update(TextDocument document, IReadOnlyList<LspDiagnostic> diagnostics)
    {
        _document    = document;
        _diagnostics = diagnostics;
    }

    public void Clear()
    {
        _document = null;
        _diagnostics = [];
        _visualLineMap.Clear();
    }

    public LspDiagnostic? FindDiagnosticAt(int line, int character) =>
        _diagnostics.FirstOrDefault(d =>
            d.StartLine == line &&
            character >= d.StartCharacter &&
            character <= d.EndCharacter);

    public LspDiagnostic? FindDiagnosticForLine(int line, int character)
    {
        var diagnostics = _diagnostics
            .Where(d => d.StartLine <= line && d.EndLine >= line)
            .OrderBy(d => ContainsPosition(d, line, character) ? 0 : 1)
            .ThenBy(d => d.Severity)
            .ThenBy(d => d.StartLine)
            .ThenBy(d => d.StartCharacter);

        return diagnostics.FirstOrDefault();
    }

    private static bool ContainsPosition(LspDiagnostic diagnostic, int line, int character)
    {
        if (line < diagnostic.StartLine || line > diagnostic.EndLine)
            return false;

        if (line == diagnostic.StartLine && character < diagnostic.StartCharacter)
            return false;

        if (line == diagnostic.EndLine && character > diagnostic.EndCharacter)
            return false;

        return true;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || _document is null || _diagnostics.Count == 0)
            return;

        // Reuse dictionary to avoid per-frame allocation
        _visualLineMap.Clear();
        foreach (var vl in textView.VisualLines)
            _visualLineMap[vl.FirstDocumentLine.LineNumber] = vl;

        foreach (var diag in _diagnostics)
        {
            var startLine = diag.StartLine + 1; // LSP 0-based → AvaloniaEdit 1-based
            if (startLine < 1 || startLine > _document.LineCount)
                continue;

            if (!_visualLineMap.TryGetValue(startLine, out var visualLine))
                continue;

            var pen = diag.Severity == LspDiagnosticSeverity.Error ? ErrorPen : WarningPen;

            // Compute x span from actual start/end character positions
            var startCol = diag.StartCharacter + 1;
            var endLine  = diag.EndLine + 1;
            var endCol   = endLine != startLine
                ? _document.GetLineByNumber(startLine).Length + 1
                : diag.EndCharacter + 1;

            var startPos = new TextViewPosition(startLine, startCol);
            var endPos   = new TextViewPosition(startLine, endCol);

            var x0 = textView.GetVisualPosition(startPos, VisualYPosition.LineBottom).X
                     - textView.ScrollOffset.X;
            var x1 = textView.GetVisualPosition(endPos,   VisualYPosition.LineBottom).X
                     - textView.ScrollOffset.X;

            if (x1 <= x0) x1 = x0 + 4;
            x0 = Math.Max(x0, 0);
            x1 = Math.Min(x1, textView.Bounds.Width);
            if (x1 <= 0) continue;

            var y = visualLine.VisualTop + visualLine.Height - textView.ScrollOffset.Y - 1;
            drawingContext.DrawLine(pen, new Point(x0, y), new Point(x1, y));
        }
    }
}
