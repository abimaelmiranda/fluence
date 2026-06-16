using Avalonia.Input;
using Fluence.Core.Services.Keybindings;

namespace Fluence.Modules.Settings.Services;

public static class KeyGestureFormatter
{
    public static string FromEvent(KeyEventArgs e)
    {
        var key = e.Key;
        if (key == Key.None || IsModifierKey(key))
            return string.Empty;

        var input = new KeyInput(
            key.ToString(),
            e.KeyModifiers.HasFlag(KeyModifiers.Control),
            e.KeyModifiers.HasFlag(KeyModifiers.Meta),
            e.KeyModifiers.HasFlag(KeyModifiers.Alt),
            e.KeyModifiers.HasFlag(KeyModifiers.Shift));

        return Core.Services.Keybindings.KeyGestureFormatter.Format(input);
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin;
}
