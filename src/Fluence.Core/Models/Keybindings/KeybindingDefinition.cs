namespace Fluence.Core.Models.Keybindings;

public sealed class KeybindingDefinition
{
    public string Key { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string Scope { get; set; } = KeybindingScope.Global;
}
