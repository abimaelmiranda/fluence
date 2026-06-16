using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Models.LanguageServer;

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

            if (result?["data"] is not JsonArray dataArray)
            {
                Console.Error.WriteLine($"[ST] no data — result={result?.ToJsonString()?.Substring(0, Math.Min(120, result.ToJsonString().Length))}");
                return [];
            }

            var data = new List<int>(dataArray.Count);
            foreach (var item in dataArray)
                data.Add(item?.GetValue<int>() ?? 0);

            return Decode(data, lss.SemanticTokenTypes, lss.SemanticTokenModifiers);
        }
        catch
        {
            return [];
        }
    }

    private static SemanticToken[] Decode(List<int> data, IReadOnlyList<string> tokenTypes, IReadOnlyList<string> tokenModifiers)
    {
        if (data.Count % 5 != 0)
            return [];

        var count = data.Count / 5;
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

    private static string[] DecodeModifiers(int bitmask, IReadOnlyList<string> tokenModifiers)
    {
        if (bitmask == 0 || tokenModifiers.Count == 0)
            return [];

        var result = new List<string>();
        for (var i = 0; i < tokenModifiers.Count; i++)
        {
            if ((bitmask & (1 << i)) != 0)
                result.Add(tokenModifiers[i]);
        }
        return [.. result];
    }
}
