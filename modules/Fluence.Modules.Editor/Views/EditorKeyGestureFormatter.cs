using System;
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
        var keyName = GetKeyName(e);
        if (string.IsNullOrWhiteSpace(keyName))
            return string.Empty;

        var input = new KeyInput(
            keyName,
            e.KeyModifiers.HasFlag(KeyModifiers.Control),
            e.KeyModifiers.HasFlag(KeyModifiers.Meta),
            e.KeyModifiers.HasFlag(KeyModifiers.Alt),
            e.KeyModifiers.HasFlag(KeyModifiers.Shift));

        return Core.Services.Keybindings.KeyGestureFormatter.Format(input);
    }

    public static string Normalize(string? gesture) =>
        Core.Services.Keybindings.KeyGestureFormatter.Normalize(gesture);

    private static string GetKeyName(KeyEventArgs e)
    {
        if (IsModifierKey(e.Key))
            return string.Empty;

        if (IsMacCommandPeriod(e))
            return ".";

        return Core.Services.Keybindings.KeyGestureFormatter.FirstMeaningfulKeyName(
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
