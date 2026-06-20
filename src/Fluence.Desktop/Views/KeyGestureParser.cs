using Avalonia.Input;

namespace Fluence.Desktop.Views;

internal static class KeyGestureParser
{
    public static KeyGesture? TryParse(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var parts = key.Split('+', System.StringSplitOptions.TrimEntries | System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;

        var modifiers = KeyModifiers.None;
        string? keyPart = null;

        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL": case "CONTROL": modifiers |= KeyModifiers.Control; break;
                case "META": case "CMD": case "COMMAND": modifiers |= KeyModifiers.Meta; break;
                case "ALT": case "OPTION": modifiers |= KeyModifiers.Alt; break;
                case "SHIFT": modifiers |= KeyModifiers.Shift; break;
                default: keyPart = part; break;
            }
        }

        if (keyPart is null || !System.Enum.TryParse<Key>(keyPart, ignoreCase: true, out var avKey))
            return null;

        return new KeyGesture(avKey, modifiers);
    }
}
