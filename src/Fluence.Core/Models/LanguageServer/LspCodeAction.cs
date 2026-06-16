using System.Collections.Generic;

namespace Fluence.Core.Models.LanguageServer;

public sealed record LspCodeAction(
    string Title,
    bool IsPreferred,
    LspWorkspaceEdit? Edit,
    string? CommandIdentifier,
    string? CommandArgumentsJson,
    string? RawJson = null);

public sealed record LspWorkspaceEdit(Dictionary<string, LspTextEdit[]> Changes);

public sealed record LspTextEdit(
    string NewText,
    int StartLine,
    int StartCharacter,
    int EndLine,
    int EndCharacter);
