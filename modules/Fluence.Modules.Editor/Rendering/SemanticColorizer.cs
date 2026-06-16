using System;
using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Modules.Editor.Rendering;

internal sealed class SemanticColorizer : DocumentColorizingTransformer
{
    private static readonly IReadOnlyDictionary<string, string> DefaultColors =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["type"] = "#4EC9B0",
            ["interface"] = "#B8D7A3",
            ["struct"] = "#86C691",
            ["enum"] = "#B8D7A3",
            ["enumMember"] = "#51B6C4",
            ["typeParameter"] = "#B8D7A3",
            ["method"] = "#DCDCAA",
            ["field"] = "#D4D4D4",
            ["staticSymbol"] = "#51B6C4",
            ["variable"] = "#9CDCFE",
        };

    private Dictionary<int, List<SemanticToken>> _tokensByLine = [];
    private IReadOnlyDictionary<string, IBrush> _brushes = CreateBrushes(DefaultColors);

    public void Update(SemanticToken[] tokens)
    {
        var map = new Dictionary<int, List<SemanticToken>>();
        foreach (var token in tokens)
        {
            if (!map.TryGetValue(token.Line, out var list))
                map[token.Line] = list = [];
            list.Add(token);
        }
        _tokensByLine = map;
    }

    public void ApplyTheme(IReadOnlyDictionary<string, string> semanticTokenColors)
    {
        var colors = new Dictionary<string, string>(DefaultColors, StringComparer.OrdinalIgnoreCase);
        foreach (var item in semanticTokenColors)
        {
            var key = NormalizeTokenKey(item.Key);
            if (!string.IsNullOrWhiteSpace(key))
                colors[key] = item.Value;
        }

        _brushes = CreateBrushes(colors);
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        var lineIndex = line.LineNumber - 1; // LSP 0-based
        if (!_tokensByLine.TryGetValue(lineIndex, out var lineTokens)) return;

        var lineStart  = line.Offset;
        var lineLength = line.Length;

        foreach (var token in lineTokens)
        {
            if (token.StartChar >= lineLength) continue;

            var brush = TokenTypeToBrush(token.TokenType, token.Modifiers);
            if (brush is null) continue;

            var start = lineStart + token.StartChar;
            var end   = Math.Min(start + token.Length, lineStart + lineLength);
            if (end <= start) continue;

            ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    private IBrush? TokenTypeToBrush(string tokenType, string[] modifiers)
    {
        // OmniSharp uses "staticSymbol" as a token TYPE (not modifier) for static members.
        // Standard LSP modifier "static" is also checked as fallback.
        var isStatic = tokenType == "staticSymbol" || Array.IndexOf(modifiers, "static") >= 0;

        var key = tokenType switch
        {
            // Types — OmniSharp names
            "class" or "delegateName" or "record" => "type",
            "interface"                            => "interface",
            "enum"                                 => "enum",
            "struct" or "recordStruct"             => "struct",
            "typeParameter"                        => "typeParameter",
            "namespace" or "module"                => null,

            // Members — OmniSharp names
            "method" or "extensionMethod"          => "method",
            "property"                             => null,
            "field" when isStatic                  => "staticSymbol",
            "field"                                => "field",
            "enumMember"                           => "enumMember",
            "event"                                => "method",

            // Locals — OmniSharp uses "local" for local variables
            "local" or "parameter"                 => "variable",

            // Static catch-all: when OmniSharp emits staticSymbol as the type
            "staticSymbol"                         => "staticSymbol",

            _                                      => NormalizeTokenKey(tokenType),
        };

        return key is not null && _brushes.TryGetValue(key, out var brush) ? brush : null;
    }

    private static IReadOnlyDictionary<string, IBrush> CreateBrushes(IReadOnlyDictionary<string, string> colors)
    {
        var brushes = new Dictionary<string, IBrush>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in colors)
        {
            try
            {
                brushes[item.Key] = new SolidColorBrush(Color.Parse(item.Value));
            }
            catch
            {
            }
        }

        return brushes;
    }

    private static string? NormalizeTokenKey(string tokenType) =>
        tokenType switch
        {
            "class" or "delegateName" or "record" => "type",
            "recordStruct" => "struct",
            "extensionMethod" => "method",
            "local" or "parameter" => "variable",
            _ => string.IsNullOrWhiteSpace(tokenType) ? null : tokenType,
        };
}
