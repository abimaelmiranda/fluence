namespace Fluence.Core.Debug;

public sealed record DebugStackFrame(
    int Id,
    string Name,
    string? FilePath,
    int Line);
