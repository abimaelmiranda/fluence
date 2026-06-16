namespace Fluence.Core.Models.Keybindings;

public sealed record KeybindingConflict(
    string Scope,
    string Key,
    string FirstCommand,
    string SecondCommand);
