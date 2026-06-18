namespace Fluence.Core.Models.LanguageServer;

public sealed record LspDiagnostic(
    string Message,
    LspDiagnosticSeverity Severity,
    int StartLine,
    int StartCharacter,
    int EndLine,
    int EndCharacter,
    string? Code);
