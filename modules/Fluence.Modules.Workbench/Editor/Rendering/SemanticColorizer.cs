using System;
using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Modules.Workbench.Editor.Rendering;

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
            ["methodName"] = "#DCDCAA",
            ["extensionMethodName"] = "#DCDCAA",
            ["property"] = "#D4D4D4",
            ["propertyName"] = "#D4D4D4",
            ["field"] = "#D4D4D4",
            ["fieldName"] = "#D4D4D4",
            ["constant"] = "#D4D4D4",
            ["constantName"] = "#D4D4D4",
            ["eventName"] = "#DCDCAA",
            ["staticSymbol"] = "#D4D4D4",
            ["variable"] = "#9CDCFE",
        };

    private readonly Dictionary<int, List<SemanticToken>> _tokensByLine = [];
    private readonly Stack<List<SemanticToken>> _lineTokenListPool = new();
    private readonly Dictionary<IBrush, Action<VisualLineElement>> _brushSetters = [];
    private IReadOnlyDictionary<string, IBrush> _brushes = CreateBrushes(DefaultColors);

    public void Update(SemanticToken[] tokens)
    {
        ClearTokens();
        _tokensByLine.EnsureCapacity(Math.Min(tokens.Length, 1024));
        foreach (var token in tokens)
        {
            if (!_tokensByLine.TryGetValue(token.Line, out var list))
                _tokensByLine[token.Line] = list = RentTokenList();
            list.Add(token);
        }
    }

    public void Clear()
    {
        ClearTokens();
    }

    public IBrush? GetBrush(string tokenType) =>
        _brushes.TryGetValue(tokenType, out var b) ? b : null;

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
        _brushSetters.Clear();
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

            ChangeLinePart(start, end, GetBrushSetter(brush));
        }
    }

    private List<SemanticToken> RentTokenList()
    {
        if (_lineTokenListPool.Count == 0)
            return [];

        var list = _lineTokenListPool.Pop();
        list.Clear();
        return list;
    }

    private void ClearTokens()
    {
        foreach (var list in _tokensByLine.Values)
        {
            list.Clear();
            _lineTokenListPool.Push(list);
        }

        _tokensByLine.Clear();
    }

    private Action<VisualLineElement> GetBrushSetter(IBrush brush)
    {
        if (_brushSetters.TryGetValue(brush, out var setter))
            return setter;

        setter = element => element.TextRunProperties.SetForegroundBrush(brush);
        _brushSetters[brush] = setter;
        return setter;
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
            "method" or "methodName" or
            "extensionMethod" or "extensionMethodName"
                                                   => "method",
            "property" or "propertyName"           => "property",
            "constant" or "constantName"           => "constant",
            "fieldName"                            => "field",
            "field" when isStatic                  => "staticSymbol",
            "field"                                => "field",
            "enumMember"                           => "enumMember",
            "event" or "eventName"                 => "method",

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
            "methodName" or "extensionMethod" or "extensionMethodName" => "method",
            "propertyName" => "property",
            "fieldName" => "field",
            "constantName" => "constant",
            "eventName" => "method",
            "local" or "parameter" => "variable",
            _ => string.IsNullOrWhiteSpace(tokenType) ? null : tokenType,
        };
}
