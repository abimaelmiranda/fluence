using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Modules.LanguageServer.Services;

internal sealed class SignatureHelpService(ILanguageServerService lsp, LspClientHolder holder) : ISignatureHelpService
{
    public async Task<LspSignatureHelp?> GetSignatureHelpAsync(
        string filePath,
        int line,
        int character,
        bool isRetrigger = false,
        CancellationToken cancellationToken = default)
    {
        if (!lsp.IsRunning || holder.Client is null)
            return null;

        try
        {
            var result = await holder.Client.SendRequestAsync("textDocument/signatureHelp", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = new Uri(filePath).AbsoluteUri },
                ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
                ["context"] = new JsonObject
                {
                    ["triggerKind"] = isRetrigger ? 3 : 1,
                    ["isRetrigger"] = isRetrigger,
                },
            }, cancellationToken).ConfigureAwait(false);

            return ParseSignatureHelp(result);
        }
        catch
        {
            return null;
        }
    }

    private static LspSignatureHelp? ParseSignatureHelp(JsonNode? result)
    {
        if (result is not JsonObject obj)
            return null;

        var rawSigs = obj["signatures"]?.AsArray();
        if (rawSigs is null || rawSigs.Count == 0)
            return null;

        var signatures = new List<LspSignatureInformation>();
        foreach (var raw in rawSigs)
        {
            if (raw is not JsonObject sig)
                continue;

            var label = sig["label"]?.GetValue<string>() ?? string.Empty;
            var doc = ExtractDocumentation(sig["documentation"]);
            var parameters = ParseParameters(sig["parameters"]?.AsArray());
            signatures.Add(new LspSignatureInformation(label, doc, parameters));
        }

        if (signatures.Count == 0)
            return null;

        var activeSig = obj["activeSignature"]?.GetValue<int>() ?? 0;
        var activeParam = obj["activeParameter"]?.GetValue<int>() ?? 0;

        activeSig = Math.Clamp(activeSig, 0, signatures.Count - 1);

        return new LspSignatureHelp(signatures, activeSig, activeParam);
    }

    private static IReadOnlyList<LspParameterInformation>? ParseParameters(JsonArray? rawParams)
    {
        if (rawParams is null || rawParams.Count == 0)
            return null;

        var result = new List<LspParameterInformation>();
        foreach (var raw in rawParams)
        {
            if (raw is not JsonObject p)
                continue;

            var doc = ExtractDocumentation(p["documentation"]);
            var labelNode = p["label"];
            if (labelNode is JsonArray offsets && offsets.Count == 2)
            {
                var start = offsets[0]?.GetValue<int>() ?? 0;
                var end = offsets[1]?.GetValue<int>() ?? 0;
                result.Add(new LspParameterInformation(string.Empty, doc, start, end));
            }
            else
            {
                var paramLabel = labelNode?.GetValue<string>() ?? string.Empty;
                result.Add(new LspParameterInformation(paramLabel, doc));
            }
        }

        return result.Count > 0 ? result : null;
    }

    private static string? ExtractDocumentation(JsonNode? node)
    {
        if (node is null) return null;
        if (node is JsonObject mc) return mc["value"]?.GetValue<string>();
        return node.GetValue<string>();
    }
}
