namespace Fluence.Core.Debug;

public sealed record DebugBreakpoint(
    string FilePath,
    int Line,
    bool IsEnabled = true,
    bool IsVerified = false,
    int? AdapterId = null,
    string? Message = null);
