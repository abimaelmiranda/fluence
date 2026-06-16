using System;
using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Modules.Editor.Rendering;

internal sealed class SemanticColorizer : DocumentColorizingTransformer
{
    private Dictionary<int, List<SemanticToken>> _tokensByLine = [];

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

    private static IBrush? TokenTypeToBrush(string tokenType, string[] modifiers)
    {
        // OmniSharp uses "staticSymbol" as a token TYPE (not modifier) for static members.
        // Standard LSP modifier "static" is also checked as fallback.
        var isStatic = tokenType == "staticSymbol" || Array.IndexOf(modifiers, "static") >= 0;

        return tokenType switch
        {
            // Types — OmniSharp names
            "class" or "delegateName" or "record" => SemanticBrushes.Type,
            "interface"                            => SemanticBrushes.Interface,
            "enum"                                 => SemanticBrushes.Enum,
            "struct" or "recordStruct"             => SemanticBrushes.Struct,
            "typeParameter"                        => SemanticBrushes.TypeParameter,
            "namespace" or "module"                => null,

            // Members — OmniSharp names
            "method" or "extensionMethod"          => SemanticBrushes.Method,
            "property"                             => null,
            "field" when isStatic                  => SemanticBrushes.ConstantField,
            "field"                                => SemanticBrushes.Field,
            "enumMember"                           => SemanticBrushes.EnumMember,
            "event"                                => SemanticBrushes.Method,

            // Locals — OmniSharp uses "local" for local variables
            "local" or "parameter"                 => SemanticBrushes.Variable,

            // Static catch-all: when OmniSharp emits staticSymbol as the type
            "staticSymbol"                         => SemanticBrushes.ConstantField,

            _                                      => null,
        };
    }

    private static class SemanticBrushes
    {
        public static readonly ISolidColorBrush Type          = new SolidColorBrush(Color.Parse("#4EC9B0"));
        public static readonly ISolidColorBrush Interface     = new SolidColorBrush(Color.Parse("#B8D7A3"));
        public static readonly ISolidColorBrush Struct        = new SolidColorBrush(Color.Parse("#86C691"));
        public static readonly ISolidColorBrush Enum          = new SolidColorBrush(Color.Parse("#B8D7A3"));
        public static readonly ISolidColorBrush EnumMember    = new SolidColorBrush(Color.Parse("#51B6C4"));
        public static readonly ISolidColorBrush TypeParameter = new SolidColorBrush(Color.Parse("#B8D7A3"));
        public static readonly ISolidColorBrush Method        = new SolidColorBrush(Color.Parse("#DCDCAA"));
        public static readonly ISolidColorBrush Property      = new SolidColorBrush(Color.Parse("#9CDCFE"));
        public static readonly ISolidColorBrush Field         = new SolidColorBrush(Color.Parse("#D4D4D4"));
        public static readonly ISolidColorBrush ConstantField = new SolidColorBrush(Color.Parse("#51B6C4"));
        public static readonly ISolidColorBrush Variable      = new SolidColorBrush(Color.Parse("#9CDCFE"));
    }
}
