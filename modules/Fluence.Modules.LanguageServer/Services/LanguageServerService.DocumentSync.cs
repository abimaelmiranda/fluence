using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Modules.LanguageServer.Services;

internal sealed partial class LanguageServerService
{
    public async Task SendDidOpenAsync(string filePath, string languageId, string content, CancellationToken cancellationToken = default)
    {
        var client = _client;
        if (client is null) return;

        await client.SendNotificationAsync("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = FilePathToUri(filePath),
                ["languageId"] = languageId,
                ["version"] = 1,
                ["text"] = content,
            },
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendDidChangeAsync(string filePath, int version, string content, CancellationToken cancellationToken = default)
    {
        var client = _client;
        if (client is null) return;

        await client.SendNotificationAsync("textDocument/didChange", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = FilePathToUri(filePath),
                ["version"] = version,
            },
            ["contentChanges"] = new JsonArray
            {
                new JsonObject { ["text"] = content },
            },
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendDidCloseAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var client = _client;
        if (client is null) return;

        await client.SendNotificationAsync("textDocument/didClose", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = FilePathToUri(filePath) },
        }, cancellationToken).ConfigureAwait(false);
    }
}
