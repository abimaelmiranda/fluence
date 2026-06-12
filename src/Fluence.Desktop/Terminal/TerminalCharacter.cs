using Avalonia.Media;

namespace Fluence.Desktop.Terminal;

public struct TerminalCharacter
{
    public char Glyph;
    public Color Foreground;
    public Color Background;
    public bool Bold;

    public static readonly TerminalCharacter Empty = new()
    {
        Glyph = ' ',
        Foreground = Color.Parse("#F2F5F8"),
        Background = Colors.Transparent,
    };
}
