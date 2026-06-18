namespace Fluence.Core.Models.LanguageServer;

public sealed record LspCompletion(
    string Label,
    string InsertText,
    string? Detail,
    string? Documentation,
    LspCompletionKind Kind,
    bool IsSnippet,
    string? SortText = null,
    bool IsPreselected = false);
