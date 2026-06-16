using System;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Modules.Editor.Completion;

internal sealed partial class LspCompletionData
{
    private readonly LspCompletion _completion;

    public LspCompletionData(LspCompletion completion, double priority)
    {
        _completion = completion;
        Priority = priority;
        Text = completion.Label;
    }

    public string Text { get; }
    public object Content => CreateContent(_completion);
    public double Priority { get; }

    public bool MatchesPrefix(string prefix) =>
        _completion.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
        (_completion.InsertText ?? _completion.Label).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrWhiteSpace(_completion.SortText) &&
         _completion.SortText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        var text = _completion.InsertText ?? _completion.Label;
        if (_completion.IsSnippet && _completion.InsertText is not null)
        {
            // Strip LSP snippet placeholders for basic insertion
            text = SnippetPlaceholderRegex().Replace(text, m =>
                m.Groups[1].Success ? m.Groups[1].Value : string.Empty);
        }
        text = AngleBracketTagRegex().Replace(text, string.Empty);
        ReplaceCurrentCompletionPrefix(textArea, text);
    }

    [GeneratedRegex(@"\$\{?\d+:?([^}]*)?\}?|\$0")]
    private static partial Regex SnippetPlaceholderRegex();

    [GeneratedRegex(@"<[^<>]*>")]
    private static partial Regex AngleBracketTagRegex();

    private static void ReplaceCurrentCompletionPrefix(TextArea textArea, string text)
    {
        var document    = textArea.Document;
        var caretOffset = textArea.Caret.Offset;
        var startOffset = caretOffset;

        while (startOffset > 0 && IsCompletionChar(document.GetCharAt(startOffset - 1)))
            startOffset--;

        document.Replace(startOffset, caretOffset - startOffset, text);
        textArea.Caret.Offset = startOffset + text.Length;
    }

    private static bool IsCompletionChar(char ch) => char.IsLetterOrDigit(ch) || ch == '_';

    private static Control CreateContent(LspCompletion completion)
    {
        var kind = GetKindLabel(completion.Kind);

        var label = new TextBlock
        {
            Text = completion.Label,
            FontFamily = new FontFamily("Menlo,Consolas,Cascadia Mono,monospace"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var kindBadge = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x34)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x50)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 1),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = kind,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xD0)),
            },
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(label,    0);
        Grid.SetColumn(kindBadge, 1);
        grid.Children.Add(label);
        grid.Children.Add(kindBadge);

        return grid;
    }

    private static string GetKindLabel(LspCompletionKind kind) =>
        kind switch
        {
            LspCompletionKind.Method        => "method",
            LspCompletionKind.Function      => "function",
            LspCompletionKind.Constructor   => "ctor",
            LspCompletionKind.Field         => "field",
            LspCompletionKind.Variable      => "var",
            LspCompletionKind.Class         => "class",
            LspCompletionKind.Interface     => "interface",
            LspCompletionKind.Module        => "module",
            LspCompletionKind.Property      => "prop",
            LspCompletionKind.Enum          => "enum",
            LspCompletionKind.EnumMember    => "enum member",
            LspCompletionKind.Struct        => "struct",
            LspCompletionKind.Event         => "event",
            LspCompletionKind.Keyword       => "keyword",
            LspCompletionKind.Snippet       => "snippet",
            LspCompletionKind.Constant      => "const",
            LspCompletionKind.TypeParameter => "typeparam",
            LspCompletionKind.Reference     => "ref",
            LspCompletionKind.File          => "file",
            LspCompletionKind.Folder        => "folder",
            LspCompletionKind.Color         => "color",
            LspCompletionKind.Operator      => "operator",
            LspCompletionKind.Value         => "value",
            LspCompletionKind.Unit          => "unit",
            _                               => "text",
        };
}
