using System;
using Avalonia.Input;
using Fluence.Core.Services.Keybindings;

namespace Fluence.Modules.Settings.Services;

public static class KeyGestureFormatter
{
    public static string FromEvent(KeyEventArgs e)
    {
        var keyName = GetKeyName(e);
        if (string.IsNullOrWhiteSpace(keyName))
            return string.Empty;

        var input = new KeyInput(
            keyName,
            e.KeyModifiers.HasFlag(KeyModifiers.Control),
            e.KeyModifiers.HasFlag(KeyModifiers.Meta),
            e.KeyModifiers.HasFlag(KeyModifiers.Alt),
            e.KeyModifiers.HasFlag(KeyModifiers.Shift));

        return Fluence.Core.Services.Keybindings.KeyGestureFormatter.Format(input);
    }

    public static bool IsCancelCapture(KeyEventArgs e) =>
        e.Key == Key.Escape && !IsMacCommandPeriod(e);

    private static string GetKeyName(KeyEventArgs e)
    {
        if (IsModifierKey(e.Key))
            return string.Empty;

        if (IsMacCommandPeriod(e))
            return ".";

        return Fluence.Core.Services.Keybindings.KeyGestureFormatter.FirstMeaningfulKeyName(
            e.Key == Key.None ? null : e.Key.ToString(),
            e.PhysicalKey == PhysicalKey.None ? null : e.PhysicalKey.ToString(),
            e.KeySymbol);
    }

    private static bool IsMacCommandPeriod(KeyEventArgs e) =>
        OperatingSystem.IsMacOS() &&
        e.Key == Key.Escape &&
        e.KeyModifiers.HasFlag(KeyModifiers.Meta);

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin;
}
