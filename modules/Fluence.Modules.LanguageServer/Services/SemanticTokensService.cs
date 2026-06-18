using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Models.LanguageServer;
using Fluence.Modules.LanguageServer.Protocol;

namespace Fluence.Modules.LanguageServer.Services;

internal sealed class SemanticTokensService(LanguageServerService lss, LspClientHolder holder)
{
    public async Task<SemanticToken[]> RequestAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!lss.IsRunning || holder.Client is null)
            return [];

        try
        {
            var result = await holder.Client.SendRequestAsync("textDocument/semanticTokens/full", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = new Uri(filePath).AbsoluteUri },
            }, cancellationToken).ConfigureAwait(false);

            var raw = result?.Deserialize(LspJsonContext.Default.LspSemanticTokensRaw);
            if (raw?.Data is null or { Length: 0 })
            {
                return [];
            }

            return Decode(raw.Data, lss.SemanticTokenTypes, lss.SemanticTokenModifiers);
        }
        catch
        {
            return [];
        }
    }

    private static SemanticToken[] Decode(int[] data, System.Collections.Generic.IReadOnlyList<string> tokenTypes, System.Collections.Generic.IReadOnlyList<string> tokenModifiers)
    {
        if (data.Length % 5 != 0)
            return [];

        var count = data.Length / 5;
        var tokens = new SemanticToken[count];
        var currentLine = 0;
        var currentChar = 0;

        for (var i = 0; i < count; i++)
        {
            var deltaLine = data[i * 5];
            var deltaStart = data[i * 5 + 1];
            var length = data[i * 5 + 2];
            var tokenTypeIndex = data[i * 5 + 3];
            var tokenModifiersBitmask = data[i * 5 + 4];

            if (deltaLine != 0)
            {
                currentLine += deltaLine;
                currentChar = deltaStart;
            }
            else
            {
                currentChar += deltaStart;
            }

            var tokenType = tokenTypeIndex < tokenTypes.Count ? tokenTypes[tokenTypeIndex] : string.Empty;
            var modifiers = DecodeModifiers(tokenModifiersBitmask, tokenModifiers);

            tokens[i] = new SemanticToken(currentLine, currentChar, length, tokenType, modifiers);
        }

        return tokens;
    }

    private static string[] DecodeModifiers(int bitmask, System.Collections.Generic.IReadOnlyList<string> tokenModifiers)
    {
        if (bitmask == 0 || tokenModifiers.Count == 0)
            return [];

        var result = new System.Collections.Generic.List<string>();
        for (var i = 0; i < tokenModifiers.Count; i++)
        {
            if ((bitmask & (1 << i)) != 0)
                result.Add(tokenModifiers[i]);
        }
        return [.. result];
    }
}
