using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Fluence.Modules.Terminal.Terminal;

public sealed class TerminalControl : Control
{
    public static readonly StyledProperty<TerminalBuffer?> BufferProperty =
        AvaloniaProperty.Register<TerminalControl, TerminalBuffer?>(nameof(Buffer));

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<TerminalControl, FontFamily>(nameof(FontFamily), new FontFamily("Menlo,Cascadia Mono,Consolas,monospace"));

    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<TerminalControl, double>(nameof(FontSize), 13.0);

    private double _charWidth;
    private double _charHeight;
    private bool _metricsValid;
    private int _lastCols;
    private int _lastRows;

    public event Func<string, Task>? TerminalTextInput;
    public event Action<int, int>? Resized;

    public TerminalBuffer? Buffer
    {
        get => GetValue(BufferProperty);
        set => SetValue(BufferProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    static TerminalControl()
    {
        AffectsRender<TerminalControl>(BufferProperty, FontFamilyProperty, FontSizeProperty);
        FocusableProperty.OverrideDefaultValue<TerminalControl>(true);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BufferProperty)
        {
            if (change.OldValue is TerminalBuffer old)
                old.Updated -= OnBufferUpdated;
            if (change.NewValue is TerminalBuffer nb)
                nb.Updated += OnBufferUpdated;
            _metricsValid = false;
            InvalidateVisual();
        }

        if (change.Property == FontSizeProperty || change.Property == FontFamilyProperty)
        {
            _metricsValid = false;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureMetrics();
        return (double.IsInfinity(availableSize.Width) || double.IsInfinity(availableSize.Height))
            ? new Size(_charWidth * 80, _charHeight * 24)
            : availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        EnsureMetrics();

        if (_charWidth > 0 && _charHeight > 0)
        {
            int cols = Math.Max(1, (int)(finalSize.Width / _charWidth));
            int rows = Math.Max(1, (int)(finalSize.Height / _charHeight));
            if (cols != _lastCols || rows != _lastRows)
            {
                _lastCols = cols;
                _lastRows = rows;
                Resized?.Invoke(cols, rows);
            }
        }

        return finalSize;
    }

    public void RequestInitialResize()
    {
        EnsureMetrics();
        if (_charWidth > 0 && _charHeight > 0 && Bounds.Width > 0 && Bounds.Height > 0)
        {
            int cols = Math.Max(1, (int)(Bounds.Width / _charWidth));
            int rows = Math.Max(1, (int)(Bounds.Height / _charHeight));
            _lastCols = cols;
            _lastRows = rows;
            Resized?.Invoke(cols, rows);
        }
        else
        {
            _lastCols = 0;
            _lastRows = 0;
            InvalidateArrange();
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var buffer = Buffer;
        if (buffer is null) return;

        EnsureMetrics();
        if (_charWidth <= 0 || _charHeight <= 0) return;

        var snap = buffer.TakeSnapshot();
        ctx.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        var typeface = new Typeface(FontFamily);
        var fontSize = FontSize;

        for (int row = 0; row < snap.Rows; row++)
        {
            for (int col = 0; col < snap.Columns; col++)
            {
                var ch = snap.Cells[row, col];
                if (ch.Glyph == ' ' && ch.Background == Colors.Transparent) continue;

                double x = col * _charWidth;
                double y = row * _charHeight;

                if (ch.Background != Colors.Transparent)
                    ctx.FillRectangle(new SolidColorBrush(ch.Background), new Rect(x, y, _charWidth, _charHeight));

                if (ch.Glyph == ' ') continue;

                var ft = new FormattedText(
                    ch.Glyph.ToString(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    new SolidColorBrush(ch.Foreground));

                ctx.DrawText(ft, new Point(x, y));
            }
        }

        if (snap.CursorX < snap.Columns && snap.CursorY < snap.Rows)
        {
            double cx = snap.CursorX * _charWidth;
            double cy = snap.CursorY * _charHeight;
            ctx.FillRectangle(
                new SolidColorBrush(Color.FromArgb(180, 242, 245, 248)),
                new Rect(cx, cy, _charWidth, _charHeight));

            var cursorChar = snap.Cells[snap.CursorY, snap.CursorX];
            if (cursorChar.Glyph != ' ')
            {
                var ft = new FormattedText(
                    cursorChar.Glyph.ToString(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    new SolidColorBrush(Color.Parse("#1A1D23")));
                ctx.DrawText(ft, new Point(cx, cy));
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var text = e.Key switch
        {
            Key.Enter => "\r",
            Key.Back => "\x7F",
            Key.Tab => "\t",
            Key.Escape => "\x1B",
            Key.Up => "\x1B[A",
            Key.Down => "\x1B[B",
            Key.Right => "\x1B[C",
            Key.Left => "\x1B[D",
            Key.Home => "\x1B[H",
            Key.End => "\x1B[F",
            Key.Delete => "\x1B[3~",
            Key.F1 => "\x1BOP",
            Key.F2 => "\x1BOQ",
            Key.F3 => "\x1BOR",
            Key.F4 => "\x1BOS",
            Key.F5 => "\x1B[15~",
            Key.F6 => "\x1B[17~",
            Key.F7 => "\x1B[18~",
            Key.F8 => "\x1B[19~",
            Key.F9 => "\x1B[20~",
            Key.F10 => "\x1B[21~",
            Key.F11 => "\x1B[23~",
            Key.F12 => "\x1B[24~",
            _ => null
        };

        if (text is null && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            text = e.Key switch
            {
                Key.C => "\x03",
                Key.D => "\x04",
                Key.Z => "\x1A",
                Key.L => "\x0C",
                Key.U => "\x15",
                Key.W => "\x17",
                Key.A => "\x01",
                Key.E => "\x05",
                _ => null
            };
        }

        if (text is not null)
        {
            e.Handled = true;
            TerminalTextInput?.Invoke(text);
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!string.IsNullOrEmpty(e.Text))
        {
            e.Handled = true;
            TerminalTextInput?.Invoke(e.Text);
        }
    }

    private void OnBufferUpdated(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual, Avalonia.Threading.DispatcherPriority.Render);
    }

    private void EnsureMetrics()
    {
        if (_metricsValid) return;

        var ft = new FormattedText(
            "M",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily),
            FontSize,
            Brushes.White);

        _charWidth = ft.Width;
        _charHeight = ft.Height;
        _metricsValid = true;
    }
}
