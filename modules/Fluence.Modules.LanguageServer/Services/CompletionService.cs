using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Modules.LanguageServer.Services;

internal sealed class CompletionService(ILanguageServerService lsp, LspClientHolder holder) : ICompletionService
{
    public async Task<IReadOnlyList<LspCompletion>> GetCompletionsAsync(
        string filePath,
        int line,
        int character,
        CancellationToken cancellationToken = default)
    {
        if (!lsp.IsRunning || holder.Client is null)
            return [];

        try
        {
            var result = await holder.Client.SendRequestAsync("textDocument/completion", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = FilePathToUri(filePath) },
                ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
                ["context"] = new JsonObject { ["triggerKind"] = 1 },
            }, cancellationToken).ConfigureAwait(false);

            return ParseCompletions(result);
        }
        catch
        {
            return [];
        }
    }

    private static IReadOnlyList<LspCompletion> ParseCompletions(JsonNode? result)
    {
        if (result is null)
            return [];

        var items = result is JsonObject obj
            ? obj["items"]?.AsArray()
            : result.AsArray();

        if (items is null)
            return [];

        var completions = new List<LspCompletion>(items.Count);
        foreach (var item in items)
        {
            if (item is not JsonObject node)
                continue;

            var label = node["label"]?.GetValue<string>() ?? string.Empty;
            var insertText = node["insertText"]?.GetValue<string>() ?? label;
            var detail = node["detail"]?.GetValue<string>();
            var documentation = node["documentation"] is JsonObject docObj
                ? docObj["value"]?.GetValue<string>()
                : node["documentation"]?.GetValue<string>();
            var kind = (LspCompletionKind)(node["kind"]?.GetValue<int>() ?? 1);
            var isSnippet = node["insertTextFormat"]?.GetValue<int>() == 2;
            var sortText = node["sortText"]?.GetValue<string>();
            var isPreselected = node["preselect"]?.GetValue<bool>() == true;

            completions.Add(new LspCompletion(label, insertText, detail, documentation, kind, isSnippet, sortText, isPreselected));
        }

        return completions;
    }

    private static string FilePathToUri(string path) =>
        new Uri(path).AbsoluteUri;
}
