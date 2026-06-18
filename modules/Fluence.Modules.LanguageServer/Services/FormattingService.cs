using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Modules.LanguageServer.Services;

internal sealed class FormattingService(ILanguageServerService lsp, LspClientHolder holder) : IFormattingService
{
    public async Task<IReadOnlyList<LspTextEdit>> FormatDocumentAsync(
        string filePath,
        string content,
        int version,
        CancellationToken cancellationToken = default)
    {
        if (!lsp.IsRunning || holder.Client is null)
            return [];

        try
        {
            await lsp.SendDidChangeAsync(filePath, version, content, cancellationToken)
                .ConfigureAwait(false);

            var result = await holder.Client.SendRequestAsync("textDocument/formatting", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = new Uri(filePath).AbsoluteUri },
                ["options"] = new JsonObject
                {
                    ["tabSize"] = 4,
                    ["insertSpaces"] = true,
                    ["trimTrailingWhitespace"] = true,
                    ["insertFinalNewline"] = true,
                    ["trimFinalNewlines"] = true,
                },
            }, cancellationToken).ConfigureAwait(false);

            return ParseTextEdits(result);
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<LspTextEdit> ParseTextEdits(JsonNode? result)
    {
        if (result is not JsonArray editsArray)
            return [];

        var edits = new List<LspTextEdit>(editsArray.Count);
        foreach (var item in editsArray)
        {
            if (item is not JsonObject editItem)
                continue;

            var newText = editItem["newText"]?.GetValue<string>() ?? string.Empty;
            if (editItem["range"] is not JsonObject range)
                continue;

            var start = range["start"] as JsonObject;
            var end = range["end"] as JsonObject;
            if (start is null || end is null)
                continue;

            edits.Add(new LspTextEdit(
                newText,
                start["line"]?.GetValue<int>() ?? 0,
                start["character"]?.GetValue<int>() ?? 0,
                end["line"]?.GetValue<int>() ?? 0,
                end["character"]?.GetValue<int>() ?? 0));
        }

        return edits;
    }
}
