namespace Fluence.Core.Debug;

public sealed record DebugVariable(
    string Name,
    string Value,
    string Type,
    int VariablesReference = 0);
