using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Models.LanguageServer;
using Fluence.Modules.LanguageServer.Protocol;

namespace Fluence.Modules.LanguageServer.Services;

internal sealed class NavigationService(ILanguageServerService lsp, LspClientHolder holder) : INavigationService
{
    public Task<LspLocation?> GetDefinitionAsync(string filePath, int line, int character, CancellationToken cancellationToken = default) =>
        SendNavigationRequestAsync("textDocument/definition", filePath, line, character, cancellationToken);

    public Task<LspLocation?> GetImplementationAsync(string filePath, int line, int character, CancellationToken cancellationToken = default) =>
        SendNavigationRequestAsync("textDocument/implementation", filePath, line, character, cancellationToken);

    public async Task<LspLocation?> GetTypeDefinitionAsync(string filePath, int line, int character, CancellationToken cancellationToken = default)
    {
        var result = await SendNavigationRequestAsync("textDocument/typeDefinition", filePath, line, character, cancellationToken);
        return result ?? await SendNavigationRequestAsync("textDocument/definition", filePath, line, character, cancellationToken);
    }

    private async Task<LspLocation?> SendNavigationRequestAsync(
        string method,
        string filePath,
        int line,
        int character,
        CancellationToken cancellationToken)
    {
        if (!lsp.IsRunning || holder.Client is null)
            return null;

        try
        {
            var result = await holder.Client.SendRequestAsync(method, new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = FilePathToUri(filePath) },
                ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
            }, cancellationToken).ConfigureAwait(false);

            return ParseLocation(result);
        }
        catch
        {
            return null;
        }
    }

    private static LspLocation? ParseLocation(JsonNode? result)
    {
        if (result is null)
            return null;

        // Result can be Location | Location[] | LocationLink[] — take first element if array.
        LspLocationRaw? raw;
        if (result is JsonArray arr)
            raw = arr.Count > 0 ? arr[0]?.Deserialize(LspJsonContext.Default.LspLocationRaw) : null;
        else
            raw = result.Deserialize(LspJsonContext.Default.LspLocationRaw);

        if (raw?.Uri is null)
            return null;

        return new LspLocation(UriToFilePath(raw.Uri), raw.Range.Start.Line, raw.Range.Start.Character);
    }

    private static string FilePathToUri(string path) =>
        new Uri(path).AbsoluteUri;

    private static string UriToFilePath(string uri) =>
        new Uri(uri).LocalPath;
}
