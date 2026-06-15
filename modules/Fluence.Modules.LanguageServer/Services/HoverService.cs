using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Modules.LanguageServer.Services;

internal sealed class HoverService(ILanguageServerService lsp, LspClientHolder holder) : IHoverService
{
    public async Task<LspHover?> GetHoverAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        if (!lsp.IsRunning || holder.Client is null)
            return null;

        try
        {
            var result = await holder.Client.SendRequestAsync("textDocument/hover", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = new Uri(filePath).AbsoluteUri },
                ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
            }, cancellationToken).ConfigureAwait(false);

            return ParseHover(result);
        }
        catch
        {
            return null;
        }
    }

    private static LspHover? ParseHover(JsonNode? result)
    {
        if (result is not JsonObject obj)
            return null;

        var contents = obj["contents"];
        if (contents is null)
            return null;

        var text = StripCodeFences(ExtractText(contents));
        return string.IsNullOrWhiteSpace(text) ? null : new LspHover(text);
    }

    private static string? StripCodeFences(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        // Remove ```lang and ``` fence lines, keeping the content inside
        text = Regex.Replace(text, @"^```[a-zA-Z]*\r?$", string.Empty, RegexOptions.Multiline);
        return text.Trim();
    }

    private static string? ExtractText(JsonNode node)
    {
        // MarkupContent: { "kind": "markdown"|"plaintext", "value": "..." }
        if (node is JsonObject mc && mc["value"] is { } val)
            return val.GetValue<string>();

        // Plain string
        if (node.GetValueKind() == JsonValueKind.String)
            return node.GetValue<string>();

        // MarkedString array: [{ "language": "csharp", "value": "..." }, "plain text", ...]
        if (node is JsonArray arr)
        {
            var parts = arr.Select(item =>
                item is JsonObject o ? o["value"]?.GetValue<string>() : item?.GetValue<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t));
            return string.Join("\n\n", parts);
        }

        return null;
    }
}
