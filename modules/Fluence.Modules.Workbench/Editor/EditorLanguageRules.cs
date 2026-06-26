using System;

namespace Fluence.Modules.Workbench.Editor;

internal enum EditorCommentSyntax
{
    None,
    CStyle,
}

internal sealed class EditorLanguageRules(bool autoPair, bool smartEnter, EditorCommentSyntax commentSyntax)
{
    public static readonly EditorLanguageRules None = new(false, false, EditorCommentSyntax.None);
    public static readonly EditorLanguageRules CStyle = new(true, true, EditorCommentSyntax.CStyle);

    public bool AutoPair { get; } = autoPair;
    public bool SmartEnter { get; } = smartEnter;
    public EditorCommentSyntax CommentSyntax { get; } = commentSyntax;

    public bool TryGetAutoPairCloser(char opener, out char closer)
    {
        closer = opener switch
        {
            '(' => ')',
            '[' => ']',
            '{' => '}',
            _ => '\0',
        };

        return AutoPair && closer != '\0';
    }

    public bool IsAutoPairClosingChar(char ch) =>
        AutoPair && (ch is ')' or ']' or '}');
}

internal static class EditorLanguageRuleCatalog
{
    public static EditorLanguageRules Get(string? languageId) =>
        string.Equals(languageId, "csharp", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(languageId, "c", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(languageId, "cpp", StringComparison.OrdinalIgnoreCase)
            ? EditorLanguageRules.CStyle
            : EditorLanguageRules.None;
}
