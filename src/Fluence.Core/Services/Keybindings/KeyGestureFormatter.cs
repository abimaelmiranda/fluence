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
        var keyName = NormalizeKeyName(input.KeyName);
        if (string.IsNullOrWhiteSpace(keyName) || IsModifier(keyName))
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

        return prefix + keyName;
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

    private static string NormalizePart(string part)
    {
        if (part.Equals("cmd", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("command", StringComparison.OrdinalIgnoreCase))
            return "META";

        if (part.Equals("control", StringComparison.OrdinalIgnoreCase))
            return "CTRL";

        if (part.Equals("esc", StringComparison.OrdinalIgnoreCase))
            return "ESCAPE";

        return NormalizeKeyName(part).ToUpperInvariant();
    }

    public static string NormalizeKeyName(string? keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName) ||
            keyName.Equals("None", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var trimmed = keyName.Trim();
        return trimmed.ToLowerInvariant() switch
        {
            "esc" => "Escape",
            "escape" => "Escape",
            "return" => "Enter",
            "enter" => "Enter",
            "oemcomma" => "Comma",
            "numpadcomma" => "Comma",
            "oemperiod" => ".",
            "decimal" => ".",
            "period" => ".",
            "numpaddecimal" => ".",
            "." => ".",
            "oemplus" => "+",
            "plus" => "+",
            "add" => "+",
            "numpadadd" => "+",
            "oemminus" => "-",
            "minus" => "-",
            "subtract" => "-",
            "numpadsubtract" => "-",
            _ => trimmed,
        };
    }

    public static string FirstMeaningfulKeyName(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var normalized = NormalizeKeyName(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;
        }

        return string.Empty;
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
                or "LeftAlt" or "RightAlt" or "LWin" or "RWin"
                or "ControlLeft" or "ControlRight" or "ShiftLeft" or "ShiftRight"
                or "AltLeft" or "AltRight" or "MetaLeft" or "MetaRight" => true,
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
