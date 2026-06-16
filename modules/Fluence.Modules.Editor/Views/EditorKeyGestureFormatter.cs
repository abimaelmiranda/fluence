using Avalonia.Input;
using Fluence.Core.Services.Keybindings;

namespace Fluence.Modules.Editor.Views;

/// <summary>
/// Thin Avalonia adapter that translates a <see cref="KeyEventArgs"/> into a
/// framework-agnostic <see cref="KeyInput"/> and delegates to the shared
/// <see cref="KeyGestureFormatter"/> in Core. The actual gesture logic lives
/// in a single place to avoid duplication across layers.
/// </summary>
internal static class EditorKeyGestureFormatter
{
    public static string FromEvent(KeyEventArgs e)
    {
        var key = e.Key;
        if (key == Key.None)
            return string.Empty;

        var input = new KeyInput(
            key.ToString(),
            e.KeyModifiers.HasFlag(KeyModifiers.Control),
            e.KeyModifiers.HasFlag(KeyModifiers.Meta),
            e.KeyModifiers.HasFlag(KeyModifiers.Alt),
            e.KeyModifiers.HasFlag(KeyModifiers.Shift));

        return Core.Services.Keybindings.KeyGestureFormatter.Format(input);
    }

    public static string Normalize(string? gesture) =>
        Core.Services.Keybindings.KeyGestureFormatter.Normalize(gesture);
}
