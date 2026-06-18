namespace Fluence.Core.Models.Problems;

public sealed record ProblemItem(
    string FilePath,
    int Line,
    int Character,
    ProblemSeverity Severity,
    string Source,
    string? Code,
    string Message);
