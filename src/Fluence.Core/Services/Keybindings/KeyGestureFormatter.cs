using System;
using System.Linq;

namespace Fluence.Core.Services.Keybindings;

/// <summary>
/// Normalizes keyboard gestures into a canonical string form (e.g. "Ctrl+Shift+Enter").
/// Framework-agnostic: callers translate their input types into a <see cref="KeyInput"/>
/// and pass it to <see cref="Format"/>. This removes the need for per-layer gesture
/// formatters (Desktop and Editor previously each had their own copy).
/// </summary>
public static class KeyGestureFormatter
{
    public static string Format(KeyInput input)
    {
        if (string.IsNullOrWhiteSpace(input.KeyName) || IsModifier(input.KeyName))
            return string.Empty;

        var prefix = string.Empty;
        if (input.Control)
            prefix += "Ctrl+";
        if (input.Meta)
            prefix += "Meta+";
        if (input.Alt)
            prefix += "Alt+";
        if (input.Shift)
            prefix += "Shift+";

        return prefix + NormalizeKeyName(input.KeyName);
    }

    /// <summary>
    /// Normalizes an arbitrary gesture string ("Ctrl+S", "cmd+shift+p", ...) into a
    /// canonical, ordered form. Used when comparing a stored gesture against a
    /// captured one regardless of casing or modifier order.
    /// </summary>
    public static string Normalize(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
            return string.Empty;

        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizePart)
            .ToArray();

        var modifiers = parts
            .Where(IsModifier)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(ModifierOrder)
            .ToArray();

        var mainKey = parts.LastOrDefault(part => !IsModifier(part)) ?? string.Empty;

        return string.Join(
            '+',
            modifiers.Concat(string.IsNullOrWhiteSpace(mainKey) ? Array.Empty<string>() : new[] { mainKey }));
    }

    private static string NormalizeKeyName(string keyName) =>
        keyName switch
        {
            "Escape" => "Escape",
            "Return" => "Enter",
            "OemComma" => "Comma",
            "OemPlus" => "+",
            "OemMinus" => "-",
            _ => keyName,
        };

    private static string NormalizePart(string part)
    {
        if (part.Equals("cmd", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("command", StringComparison.OrdinalIgnoreCase))
            return "META";

        if (part.Equals("control", StringComparison.OrdinalIgnoreCase))
            return "CTRL";

        if (part.Equals("esc", StringComparison.OrdinalIgnoreCase))
            return "ESCAPE";

        return part.ToUpperInvariant();
    }

    private static bool IsModifier(string part) =>
        part switch
        {
            "CTRL" => true,
            "META" => true,
            "ALT" => true,
            "SHIFT" => true,
            // Bare modifier key names emitted by FromEvent for modifier keys themselves.
            "LeftCtrl" or "RightCtrl" or "LeftShift" or "RightShift"
                or "LeftAlt" or "RightAlt" or "LWin" or "RWin" => true,
            _ => false,
        };

    private static int ModifierOrder(string part) =>
        part switch
        {
            "CTRL" => 0,
            "META" => 1,
            "ALT" => 2,
            "SHIFT" => 3,
            _ => 4,
        };
}
