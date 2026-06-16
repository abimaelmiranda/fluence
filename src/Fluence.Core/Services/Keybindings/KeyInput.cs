namespace Fluence.Core.Services.Keybindings;

/// <summary>
/// Framework-agnostic description of a physical key press: the key name
/// (e.g. <c>"S"</c>, <c>"Return"</c>, <c>"OemComma"</c>) plus active modifiers.
/// Each UI layer adapts its own input type (<c>KeyEventArgs</c>) into this
/// record before delegating to <see cref="KeyGestureFormatter"/>.
/// </summary>
public sealed record KeyInput(string KeyName, bool Control, bool Meta, bool Alt, bool Shift);
