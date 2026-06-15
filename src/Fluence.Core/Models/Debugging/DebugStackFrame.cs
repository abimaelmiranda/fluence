namespace Fluence.Core.Models.Debugging;

public sealed record DebugStackFrame(
    int Id,
    string Name,
    string? FilePath,
    int Line);
