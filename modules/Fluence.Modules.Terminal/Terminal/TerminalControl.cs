using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using XTerm.Buffer;
using XTerm.Common;
using XTerm.Events;
using XTerm.Input;
using AvaloniaKey = Avalonia.Input.Key;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;
using XTermKey = XTerm.Input.Key;
using XTermModifiers = XTerm.Input.KeyModifiers;
using XTerminal = global::XTerm.Terminal;

namespace Fluence.Modules.Terminal.Terminal;

public sealed class TerminalControl : Avalonia.Controls.Control
{
    private const int LinesPerWheelDelta = 3;

    public static readonly StyledProperty<XTerminal?> TerminalProperty =
        AvaloniaProperty.Register<TerminalControl, XTerminal?>(nameof(Terminal));

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<TerminalControl, FontFamily>(nameof(FontFamily), new FontFamily("Menlo,Cascadia Mono,Consolas,monospace"));

    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<TerminalControl, double>(nameof(FontSize), 13.0);

    private double _charWidth;
    private double _charHeight;
    private bool _metricsValid;
    private int _lastCols;
    private int _lastRows;
    private Typeface _typefaceNormal;
    private Typeface _typefaceBold;
    private Typeface _typefaceItalic;
    private Typeface _typefaceBoldItalic;

    public Func<string, Task>? TerminalTextInput { get; set; }
    public Action<int, int>? Resized { get; set; }

    public XTerminal? Terminal
    {
        get => GetValue(TerminalProperty);
        set => SetValue(TerminalProperty, value);
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
        AffectsRender<TerminalControl>(TerminalProperty, FontFamilyProperty, FontSizeProperty);
        FocusableProperty.OverrideDefaultValue<TerminalControl>(true);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TerminalProperty)
        {
            if (change.OldValue is XTerminal old)
            {
                old.LineFed -= OnTerminalUpdated;
                old.BufferChanged -= OnTerminalBufferChanged;
                old.Scrolled -= OnTerminalUpdated;
            }

            if (change.NewValue is XTerminal nt)
            {
                nt.LineFed += OnTerminalUpdated;
                nt.BufferChanged += OnTerminalBufferChanged;
                nt.Scrolled += OnTerminalUpdated;
            }

            _metricsValid = false;
            _lastCols = 0;
            _lastRows = 0;
            InvalidateArrange();
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
            var (cols, rows) = GetCurrentGridSize();
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

    public (int Columns, int Rows) GetCurrentGridSize()
    {
        EnsureMetrics();

        if (_charWidth <= 0 || _charHeight <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
            return (80, 24);

        return (
            Math.Max(1, (int)(Bounds.Width / _charWidth)),
            Math.Max(1, (int)(Bounds.Height / _charHeight)));
    }

    public override void Render(DrawingContext ctx)
    {
        try
        {
            RenderTerminal(ctx);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            ctx.FillRectangle(new SolidColorBrush(Color.Parse("#1A1D23")), new Rect(Bounds.Size));
        }
    }

    private void RenderTerminal(DrawingContext ctx)
    {
        var terminal = Terminal;
        EnsureMetrics();
        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#1A1D23")), new Rect(Bounds.Size));

        if (terminal is null || _charWidth <= 0 || _charHeight <= 0)
            return;

        var buffer = terminal.Buffer;
        var fontSize = FontSize;

        // Use buffer.YDisp (scroll offset) and buffer.Lines[] per XTerm.NET README.
        // Do NOT guard with buffer.Length — it may be 0 for a fresh terminal, causing
        // the loop to never run even after the shell has written content.
        for (int row = 0; row < terminal.Rows; row++)
        {
            var line = buffer.Lines[buffer.YDisp + row];
            if (line is null)
                continue;

            for (int col = 0; col < terminal.Cols; col++)
            {
                var cell = line[col];
                if (cell.Width == 0) continue; // wide-char continuation cell

                var attrs = cell.Attributes;
                var fg = ResolveFgColor(attrs);
                var bg = ResolveBgColor(attrs);

                // Inverse video: swap fg/bg. Use opaque defaults when transparent.
                if (attrs.IsInverse())
                {
                    var invFg = bg.A == 0 ? Color.Parse("#1A1D23") : bg;
                    var invBg = fg == DefaultFg ? Color.Parse("#F2F5F8") : fg;
                    fg = invFg;
                    bg = invBg;
                }

                var content = cell.Content;

                double x = col * _charWidth;
                double y = row * _charHeight;

                if (bg.A > 0)
                    ctx.FillRectangle(new SolidColorBrush(bg),
                        new Rect(x, y, _charWidth * Math.Max(1, cell.Width), _charHeight));

                if (!string.IsNullOrEmpty(content) && content != " ")
                {
                    bool bold   = attrs.IsBold();
                    bool italic = attrs.IsItalic();
                    var typeface = (bold, italic) switch
                    {
                        (true,  true)  => _typefaceBoldItalic,
                        (true,  false) => _typefaceBold,
                        (false, true)  => _typefaceItalic,
                        _              => _typefaceNormal,
                    };

                    var ft = new FormattedText(
                        content,
                        System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        new SolidColorBrush(fg));
                    ctx.DrawText(ft, new Point(x, y));
                }

                if (attrs.IsUnderline())
                {
                    double uy = y + _charHeight - 2;
                    ctx.DrawLine(
                        new Pen(new SolidColorBrush(fg), 1),
                        new Point(x, uy),
                        new Point(x + _charWidth * Math.Max(1, cell.Width), uy));
                }
            }
        }

        // Cursor — buffer.Y is viewport-relative per XTerm.NET README (not buffer.Y - YDisp)
        if (terminal.CursorVisible)
        {
            int cx = buffer.X;
            int cy = buffer.Y;
            if (cx >= 0 && cx < terminal.Cols && cy >= 0 && cy < terminal.Rows)
            {
                double px = cx * _charWidth;
                double py = cy * _charHeight;
                ctx.FillRectangle(
                    new SolidColorBrush(Color.FromArgb(200, 242, 245, 248)),
                    new Rect(px, py, _charWidth, _charHeight));

                var cursorLine = buffer.Lines[buffer.YDisp + cy];
                if (cursorLine is not null && cx < cursorLine.Length)
                {
                    var cc = cursorLine[cx];
                    if (!string.IsNullOrEmpty(cc.Content) && cc.Content != " ")
                    {
                        bool ccBold   = cc.Attributes.IsBold();
                        bool ccItalic = cc.Attributes.IsItalic();
                        var ccTypeface = (ccBold, ccItalic) switch
                        {
                            (true,  true)  => _typefaceBoldItalic,
                            (true,  false) => _typefaceBold,
                            (false, true)  => _typefaceItalic,
                            _              => _typefaceNormal,
                        };
                        var ft = new FormattedText(
                            cc.Content,
                            System.Globalization.CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            ccTypeface,
                            fontSize,
                            new SolidColorBrush(Color.Parse("#1A1D23")));
                        ctx.DrawText(ft, new Point(px, py));
                    }
                }
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var terminal = Terminal;
        if (terminal is null || e.Delta.Y == 0)
            return;

        var lines = Math.Max(1, (int)Math.Ceiling(Math.Abs(e.Delta.Y) * LinesPerWheelDelta));
        terminal.ScrollLines(e.Delta.Y > 0 ? -lines : lines);
        e.Handled = true;
        RequestRedraw();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var terminal = Terminal;
        if (terminal is null)
            return;

        var xMod = ToXTermModifiers(e.KeyModifiers);
        var xKey = ToXTermKey(e.Key);

        if (xKey is not null)
        {
            var seq = terminal.GenerateKeyInput(xKey.Value, xMod);
            if (!string.IsNullOrEmpty(seq))
            {
                e.Handled = true;
                TerminalTextInput?.Invoke(seq);
                return;
            }
        }

        // Ctrl+letter shortcuts not covered by GenerateKeyInput
        if (e.KeyModifiers.HasFlag(AvaloniaKeyModifiers.Control))
        {
            var ctrlSeq = e.Key switch
            {
                AvaloniaKey.C => terminal.GenerateCharInput('\x03', xMod),
                AvaloniaKey.D => terminal.GenerateCharInput('\x04', xMod),
                AvaloniaKey.Z => terminal.GenerateCharInput('\x1A', xMod),
                AvaloniaKey.L => terminal.GenerateCharInput('\x0C', xMod),
                AvaloniaKey.U => terminal.GenerateCharInput('\x15', xMod),
                AvaloniaKey.W => terminal.GenerateCharInput('\x17', xMod),
                AvaloniaKey.A => terminal.GenerateCharInput('\x01', xMod),
                AvaloniaKey.E => terminal.GenerateCharInput('\x05', xMod),
                _ => null,
            };
            if (ctrlSeq is not null)
            {
                e.Handled = true;
                TerminalTextInput?.Invoke(ctrlSeq);
            }
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        var terminal = Terminal;
        if (terminal is null || string.IsNullOrEmpty(e.Text))
            return;

        e.Handled = true;
        foreach (var ch in e.Text)
        {
            var seq = terminal.GenerateCharInput(ch, XTermModifiers.None);
            if (!string.IsNullOrEmpty(seq))
                TerminalTextInput?.Invoke(seq);
        }
    }

    private void OnTerminalUpdated(object? sender, EventArgs e) => RequestRedraw();
    private void OnTerminalBufferChanged(object? sender, TerminalEvents.BufferChangedEventArgs e) => RequestRedraw();

    public void RequestRedraw()
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            InvalidateVisual();
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual, Avalonia.Threading.DispatcherPriority.Render);
    }

    private void EnsureMetrics()
    {
        if (_metricsValid) return;

        _typefaceNormal   = new Typeface(FontFamily, FontStyle.Normal, FontWeight.Normal);
        _typefaceBold     = new Typeface(FontFamily, FontStyle.Normal, FontWeight.Bold);
        _typefaceItalic   = new Typeface(FontFamily, FontStyle.Italic, FontWeight.Normal);
        _typefaceBoldItalic = new Typeface(FontFamily, FontStyle.Italic, FontWeight.Bold);

        var ft = new FormattedText(
            "M",
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _typefaceNormal,
            FontSize,
            Brushes.White);

        _charWidth = ft.Width;
        _charHeight = ft.Height;
        _metricsValid = true;
    }

    // Color resolution ---------------------------------------------------

    private static readonly Color DefaultFg = Color.Parse("#F2F5F8");
    private static readonly Color DefaultBg = Colors.Transparent;

    private static Color ResolveFgColor(AttributeData attrs)
    {
        var mode = attrs.GetFgColorMode();
        var color = attrs.GetFgColor();
        return ResolveColor(color, mode, DefaultFg, isBackground: false);
    }

    private static Color ResolveBgColor(AttributeData attrs)
    {
        var mode = attrs.GetBgColorMode();
        var color = attrs.GetBgColor();
        return ResolveColor(color, mode, DefaultBg, isBackground: true);
    }

    private static Color ResolveColor(int color, int mode, Color defaultColor, bool isBackground)
    {
        // Default color sentinel: fg=256, bg=257
        var sentinel = isBackground ? Constants.DefaultAttrDataBg : Constants.DefaultAttrDataFg;
        if (color == sentinel)
            return defaultColor;

        if (mode == (int)ColorMode.RGB)
        {
            byte r = (byte)((color >> 16) & 0xFF);
            byte g = (byte)((color >> 8) & 0xFF);
            byte b = (byte)(color & 0xFF);
            return Color.FromRgb(r, g, b);
        }

        // Palette256 mode — ANSI 16 + 256-color cube
        if (color < 16)
            return Ansi16[color];

        if (color < 232)
        {
            // 6x6x6 cube: index 16-231
            int idx = color - 16;
            int b2 = idx % 6;
            int g2 = (idx / 6) % 6;
            int r2 = idx / 36;
            return Color.FromRgb(
                (byte)(r2 == 0 ? 0 : 55 + r2 * 40),
                (byte)(g2 == 0 ? 0 : 55 + g2 * 40),
                (byte)(b2 == 0 ? 0 : 55 + b2 * 40));
        }

        if (color < 256)
        {
            // Grayscale: 232-255
            byte v = (byte)(8 + (color - 232) * 10);
            return Color.FromRgb(v, v, v);
        }

        return defaultColor;
    }

    private static readonly Color[] Ansi16 =
    [
        Color.Parse("#1A1D23"), // Black
        Color.Parse("#E06C75"), // Red
        Color.Parse("#98C379"), // Green
        Color.Parse("#E5C07B"), // Yellow
        Color.Parse("#61AFEF"), // Blue
        Color.Parse("#C678DD"), // Magenta
        Color.Parse("#56B6C2"), // Cyan
        Color.Parse("#ABB2BF"), // White
        Color.Parse("#5C6370"), // Bright Black
        Color.Parse("#E06C75"), // Bright Red
        Color.Parse("#98C379"), // Bright Green
        Color.Parse("#E5C07B"), // Bright Yellow
        Color.Parse("#61AFEF"), // Bright Blue
        Color.Parse("#C678DD"), // Bright Magenta
        Color.Parse("#56B6C2"), // Bright Cyan
        Color.Parse("#F2F5F8"), // Bright White
    ];

    // Key mapping ---------------------------------------------------

    private static XTermKey? ToXTermKey(AvaloniaKey key) => key switch
    {
        AvaloniaKey.Enter => XTermKey.Enter,
        AvaloniaKey.Back => XTermKey.Backspace,
        AvaloniaKey.Tab => XTermKey.Tab,
        AvaloniaKey.Escape => XTermKey.Escape,
        AvaloniaKey.Up => XTermKey.UpArrow,
        AvaloniaKey.Down => XTermKey.DownArrow,
        AvaloniaKey.Left => XTermKey.LeftArrow,
        AvaloniaKey.Right => XTermKey.RightArrow,
        AvaloniaKey.Home => XTermKey.Home,
        AvaloniaKey.End => XTermKey.End,
        AvaloniaKey.Delete => XTermKey.Delete,
        AvaloniaKey.Insert => XTermKey.Insert,
        AvaloniaKey.PageUp => XTermKey.PageUp,
        AvaloniaKey.PageDown => XTermKey.PageDown,
        AvaloniaKey.F1 => XTermKey.F1,
        AvaloniaKey.F2 => XTermKey.F2,
        AvaloniaKey.F3 => XTermKey.F3,
        AvaloniaKey.F4 => XTermKey.F4,
        AvaloniaKey.F5 => XTermKey.F5,
        AvaloniaKey.F6 => XTermKey.F6,
        AvaloniaKey.F7 => XTermKey.F7,
        AvaloniaKey.F8 => XTermKey.F8,
        AvaloniaKey.F9 => XTermKey.F9,
        AvaloniaKey.F10 => XTermKey.F10,
        AvaloniaKey.F11 => XTermKey.F11,
        AvaloniaKey.F12 => XTermKey.F12,
        _ => null,
    };

    private static XTermModifiers ToXTermModifiers(AvaloniaKeyModifiers m)
    {
        var result = XTermModifiers.None;
        if (m.HasFlag(AvaloniaKeyModifiers.Control)) result |= XTermModifiers.Control;
        if (m.HasFlag(AvaloniaKeyModifiers.Alt)) result |= XTermModifiers.Alt;
        if (m.HasFlag(AvaloniaKeyModifiers.Shift)) result |= XTermModifiers.Shift;
        return result;
    }
}
