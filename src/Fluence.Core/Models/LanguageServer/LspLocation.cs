namespace Fluence.Core.Models.LanguageServer;

public sealed record LspLocation(
    string FilePath,
    int Line,
    int Character);
