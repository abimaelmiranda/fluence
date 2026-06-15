using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Modules.LanguageServer.Services;

internal sealed class NavigationService(ILanguageServerService lsp, LspClientHolder holder) : INavigationService
{
    public Task<LspLocation?> GetDefinitionAsync(string filePath, int line, int character, CancellationToken cancellationToken = default) =>
        SendNavigationRequestAsync("textDocument/definition", filePath, line, character, cancellationToken);

    public Task<LspLocation?> GetImplementationAsync(string filePath, int line, int character, CancellationToken cancellationToken = default) =>
        SendNavigationRequestAsync("textDocument/implementation", filePath, line, character, cancellationToken);

    public Task<LspLocation?> GetTypeDefinitionAsync(string filePath, int line, int character, CancellationToken cancellationToken = default) =>
        SendNavigationRequestAsync("textDocument/typeDefinition", filePath, line, character, cancellationToken);

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

        // Result can be Location | Location[] | LocationLink[]
        var location = result is JsonArray arr ? arr[0] : result;
        if (location is not JsonObject obj)
            return null;

        var uri = obj["uri"]?.GetValue<string>();
        var range = obj["range"]?.AsObject();
        if (uri is null || range is null)
            return null;

        var start = range["start"]?.AsObject();
        var line = start?["line"]?.GetValue<int>() ?? 0;
        var character = start?["character"]?.GetValue<int>() ?? 0;

        return new LspLocation(UriToFilePath(uri), line, character);
    }

    private static string FilePathToUri(string path) =>
        new Uri(path).AbsoluteUri;

    private static string UriToFilePath(string uri) =>
        new Uri(uri).LocalPath;
}
