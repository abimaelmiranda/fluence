using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
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
        char? triggerCharacter = null,
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
                    ["triggerKind"] = triggerCharacter.HasValue ? 2 : 1,
                    ["triggerCharacter"] = triggerCharacter?.ToString(),
                    ["isRetrigger"] = isRetrigger,
                },
            }, cancellationToken).ConfigureAwait(false);

            var signatureHelp = ParseSignatureHelp(result);
            if (signatureHelp is null)
                Debug.WriteLine($"[LS] signatureHelp empty result={result?.ToJsonString()}");
            return signatureHelp;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            Debug.WriteLine("[LS] signatureHelp request failed");
            return null;
        }
    }

    private static LspSignatureHelp? ParseSignatureHelp(JsonNode? result)
    {
        if (result is not JsonObject obj)
            return null;

        var rawSigs = TryGetArray(obj["signatures"]);
        if (rawSigs is null || rawSigs.Count == 0)
        {
            Debug.WriteLine($"[LS] signatureHelp has no signatures: {result.ToJsonString()}");
            return null;
        }

        var signatures = new List<LspSignatureInformation>();
        foreach (var raw in rawSigs)
        {
            if (raw is not JsonObject sig)
                continue;

            var label = TryGetString(sig["label"]) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(label))
                continue;

            var doc = ExtractDocumentation(sig["documentation"]);
            var parameters = ParseParameters(TryGetArray(sig["parameters"]));
            signatures.Add(new LspSignatureInformation(label, doc, parameters));
        }

        if (signatures.Count == 0)
        {
            Debug.WriteLine($"[LS] signatureHelp signatures could not be parsed: {result.ToJsonString()}");
            return null;
        }

        var activeSig = TryGetInt(obj["activeSignature"]) ?? 0;
        var activeParam = TryGetInt(obj["activeParameter"]);

        activeSig = Math.Clamp(activeSig, 0, signatures.Count - 1);
        activeParam ??= rawSigs[activeSig] is JsonObject activeSigObj
            ? TryGetInt(activeSigObj["activeParameter"])
            : null;

        return new LspSignatureHelp(signatures, activeSig, activeParam ?? 0);
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
            if (TryGetArray(labelNode) is { Count: 2 } offsets)
            {
                var start = TryGetInt(offsets[0]) ?? 0;
                var end = TryGetInt(offsets[1]) ?? 0;
                result.Add(new LspParameterInformation(string.Empty, doc, start, end));
            }
            else
            {
                var paramLabel = TryGetString(labelNode) ?? string.Empty;
                result.Add(new LspParameterInformation(paramLabel, doc));
            }
        }

        return result.Count > 0 ? result : null;
    }

    private static string? ExtractDocumentation(JsonNode? node)
    {
        if (node is null) return null;
        if (node is JsonObject mc) return TryGetString(mc["value"]);
        if (node is JsonArray arr)
        {
            var parts = arr
                .Select(ExtractDocumentation)
                .Where(text => !string.IsNullOrWhiteSpace(text));
            return string.Join("\n\n", parts);
        }
        return TryGetString(node);
    }

    private static JsonArray? TryGetArray(JsonNode? node)
    {
        try { return node as JsonArray; }
        catch { return null; }
    }

    private static string? TryGetString(JsonNode? node)
    {
        if (node is null)
            return null;

        try
        {
            if (node.GetValueKind() == JsonValueKind.String)
                return node.GetValue<string>();
            return node.ToJsonString();
        }
        catch
        {
            return null;
        }
    }

    private static int? TryGetInt(JsonNode? node)
    {
        if (node is null)
            return null;

        try { return node.GetValue<int>(); }
        catch { return null; }
    }
}
