using System;
using System.Collections.Generic;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Fluence.Modules.Workbench.Editor.Rendering;

internal static class CSharpSignatureColorizer
{
    // Fallback for tokens the theme doesn't color (keywords, annotations).
    private static readonly IBrush FallbackKeyword    = new SolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush FallbackAnnotation = new SolidColorBrush(Color.Parse("#888899"));

    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "default", "out", "ref", "in", "params", "void", "null", "true", "false",
        "async", "new", "readonly", "get", "set", "this", "init", "where",
        "abstract", "virtual", "override", "sealed", "static",
    };

    private enum Role { Annotation, Keyword, Interface, Method, Type, Variable, Default }

    public static void BuildInlines(string contents, SemanticColorizer colorizer, InlineCollection target)
    {
        if (string.IsNullOrWhiteSpace(contents))
            return;

        // Split into signature block and optional documentation block.
        var parts     = contents.Split("\n\n", 2, StringSplitOptions.None);
        var signature = parts[0].Trim();
        var docs      = parts.Length > 1 ? parts[1].Trim() : null;

        foreach (var (text, role) in Tokenize(signature))
        {
            var brush = RoleToBrush(role, colorizer);
            var run   = new Run(text);
            if (brush is not null)
                run.Foreground = brush;
            target.Add(run);
        }

        if (!string.IsNullOrWhiteSpace(docs))
        {
            target.Add(new LineBreak());
            target.Add(new LineBreak());
            var docBrush = colorizer.GetBrush("comment") ?? FallbackAnnotation;
            target.Add(new Run(docs) { Foreground = docBrush });
        }
    }

    private static IBrush? RoleToBrush(Role role, SemanticColorizer colorizer) =>
        role switch
        {
            Role.Annotation => colorizer.GetBrush("comment") ?? FallbackAnnotation,
            Role.Keyword    => colorizer.GetBrush("keyword") ?? FallbackKeyword,
            Role.Interface  => colorizer.GetBrush("interface"),
            Role.Method     => colorizer.GetBrush("method"),
            Role.Type       => colorizer.GetBrush("type"),
            Role.Variable   => colorizer.GetBrush("variable"),
            _               => null,
        };

    private static IEnumerable<(string Text, Role Role)> Tokenize(string sig)
    {
        var tokens = ScanTokens(sig);

        // Classify each identifier using look-ahead/look-behind.
        bool inAnnotation = false;
        var result = new List<(string, Role)>(tokens.Count);

        for (int i = 0; i < tokens.Count; i++)
        {
            var (text, isIdent) = tokens[i];

            if (!isIdent)
            {
                // Track annotation context: opening '(' at index 0 before any identifier.
                if (text == "(" && result.Count == 0)
                    inAnnotation = true;
                else if (text == ")" && inAnnotation)
                    inAnnotation = false;

                result.Add((text, Role.Default));
                continue;
            }

            if (inAnnotation)
            {
                result.Add((text, Role.Annotation));
                continue;
            }

            result.Add((text, ClassifyIdentifier(text, tokens, i)));
        }

        return result;
    }

    private static Role ClassifyIdentifier(string token, List<(string Text, bool IsIdent)> tokens, int index)
    {
        if (Keywords.Contains(token))
            return Role.Keyword;

        // Interface heuristic: starts with I followed by an uppercase letter.
        if (token.Length >= 2 && token[0] == 'I' && char.IsUpper(token[1]))
            return Role.Interface;

        // Look ahead: if the next significant non-whitespace token is '(' or '{' → method/property.
        for (int j = index + 1; j < tokens.Count; j++)
        {
            var (ahead, _) = tokens[j];
            if (string.IsNullOrWhiteSpace(ahead)) continue;
            if (ahead == "(" || ahead == "{")
                return Role.Method;
            break;
        }

        if (char.IsUpper(token[0]))
            return Role.Type;

        if (char.IsLower(token[0]))
            return Role.Variable;

        return Role.Default;
    }

    // Scan signature into a flat list of (text, isIdentifier) spans.
    private static List<(string Text, bool IsIdent)> ScanTokens(string sig)
    {
        var result = new List<(string, bool)>();
        int i = 0;
        while (i < sig.Length)
        {
            char c = sig[i];
            if (IsIdentStart(c))
            {
                int start = i;
                while (i < sig.Length && IsIdentChar(sig[i]))
                    i++;
                result.Add((sig[start..i], true));
            }
            else if (char.IsWhiteSpace(c))
            {
                int start = i;
                while (i < sig.Length && char.IsWhiteSpace(sig[i]))
                    i++;
                result.Add((sig[start..i], false));
            }
            else
            {
                result.Add((c.ToString(), false));
                i++;
            }
        }
        return result;
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_' || c == '@';
    private static bool IsIdentChar(char c)  => char.IsLetterOrDigit(c) || c == '_';
}
