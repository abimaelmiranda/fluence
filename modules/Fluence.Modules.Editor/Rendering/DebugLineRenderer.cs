using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using Fluence.Core.Models.Debugging;

namespace Fluence.Modules.Editor.Rendering;

internal sealed class DebugLineRenderer : IBackgroundRenderer
{
    private static readonly ISolidColorBrush ExecutionLineBrush =
        new SolidColorBrush(Color.FromArgb(96, 190, 48, 48));

    private DebugExecutionLine? _executionLine;

    public KnownLayer Layer => KnownLayer.Background;

    public void Update(DebugExecutionLine? executionLine) => _executionLine = executionLine;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || _executionLine is null)
            return;

        foreach (var line in textView.VisualLines)
        {
            if (_executionLine.Line != line.FirstDocumentLine.LineNumber) continue;
            var rect = new Rect(0, line.VisualTop - textView.ScrollOffset.Y, textView.Bounds.Width, line.Height);
            drawingContext.FillRectangle(ExecutionLineBrush, rect);
        }
    }
}
