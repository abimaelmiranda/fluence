using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.LanguageServer;
using Fluence.Infrastructure.Protocols.Lsp;

namespace Fluence.Modules.LanguageServer.Services;

internal sealed class LanguageServerService : ILanguageServerService, IAsyncDisposable
{
    private readonly ILspProvisioningService _provisioning;
    private readonly IDiagnosticsService _diagnostics;
    private readonly IShellEventBus _events;
    private readonly LspClientHolder _holder;
    private LspClient? _client;

    public bool IsRunning => _client is not null;

    public IReadOnlyList<string> SemanticTokenTypes { get; private set; } = [];
    public IReadOnlyList<string> SemanticTokenModifiers { get; private set; } = [];

    public LanguageServerService(
        ILspProvisioningService provisioning,
        IDiagnosticsService diagnostics,
        IShellEventBus events,
        LspClientHolder holder)
    {
        _provisioning = provisioning;
        _diagnostics = diagnostics;
        _events = events;
        _holder = holder;
    }

    public async Task StartAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        if (_client is not null)
            await StopAsync().ConfigureAwait(false);

        var executable = _provisioning.GetExecutablePath();
        var arguments = $"--languageserver -z -s \"{rootPath}\"";

        _client = new LspClient();
        _holder.Client = _client;
        _client.NotificationReceived += OnNotificationReceived;
        _client.Disconnected += OnClientDisconnected;

        var env = _provisioning.GetLaunchEnvironment();
        await _client.StartAsync(executable, arguments, null, env, cancellationToken).ConfigureAwait(false);

        // LSP handshake
        var initResult = await _client.SendRequestAsync("initialize", BuildInitializeParams(rootPath), cancellationToken)
            .ConfigureAwait(false);

        CaptureSemanticTokenLegend(initResult);

        await _client.SendNotificationAsync("initialized", new JsonObject(), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        if (_client is null)
            return;

        var client = _client;
        _client = null;
        _holder.Client = null;
        client.NotificationReceived -= OnNotificationReceived;

        try
        {
            await client.SendNotificationAsync("exit", null, CancellationToken.None).ConfigureAwait(false);
        }
        catch { }

        await client.DisposeAsync().ConfigureAwait(false);
    }

    public async Task SendDidOpenAsync(string filePath, string languageId, string content, CancellationToken cancellationToken = default)
    {
        if (_client is null) return;

        await _client.SendNotificationAsync("textDocument/didOpen", new JsonObject
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
        if (_client is null) return;

        await _client.SendNotificationAsync("textDocument/didChange", new JsonObject
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
        if (_client is null) return;

        await _client.SendNotificationAsync("textDocument/didClose", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = FilePathToUri(filePath) },
        }, cancellationToken).ConfigureAwait(false);
    }

    private void CaptureSemanticTokenLegend(JsonNode? initResult)
    {
        try
        {
            var legend = initResult?["capabilities"]?["semanticTokensProvider"]?["legend"];
            if (legend is not JsonObject legendObj)
                return;

            if (legendObj["tokenTypes"] is JsonArray types)
                SemanticTokenTypes = types.Select(t => t?.GetValue<string>() ?? string.Empty).ToArray();

            if (legendObj["tokenModifiers"] is JsonArray mods)
                SemanticTokenModifiers = mods.Select(m => m?.GetValue<string>() ?? string.Empty).ToArray();
        }
        catch { }
    }

    private void OnClientDisconnected()
    {
        var client = _client;
        _client = null;
        _holder.Client = null;
        if (client is not null)
            client.NotificationReceived -= OnNotificationReceived;
    }

    private void OnNotificationReceived(string method, JsonNode? parameters)
    {
        if (method == "textDocument/publishDiagnostics")
            HandlePublishDiagnostics(parameters);
    }

    private void HandlePublishDiagnostics(JsonNode? parameters)
    {
        if (parameters is not JsonObject obj)
            return;

        var uri = obj["uri"]?.GetValue<string>();
        if (uri is null)
            return;

        var filePath = UriToFilePath(uri);
        var rawDiagnostics = obj["diagnostics"]?.AsArray();
        var diagnostics = new List<LspDiagnostic>();

        if (rawDiagnostics is not null)
        {
            foreach (var raw in rawDiagnostics)
            {
                if (raw is not JsonObject d)
                    continue;

                var range = d["range"]?.AsObject();
                var start = range?["start"]?.AsObject();
                var end = range?["end"]?.AsObject();

                diagnostics.Add(new LspDiagnostic(
                    Message: d["message"]?.GetValue<string>() ?? string.Empty,
                    Severity: (LspDiagnosticSeverity)(d["severity"]?.GetValue<int>() ?? 1),
                    StartLine: start?["line"]?.GetValue<int>() ?? 0,
                    StartCharacter: start?["character"]?.GetValue<int>() ?? 0,
                    EndLine: end?["line"]?.GetValue<int>() ?? 0,
                    EndCharacter: end?["character"]?.GetValue<int>() ?? 0,
                    Code: d["code"]?.GetValue<string>()));
            }
        }

        _diagnostics.UpdateDiagnostics(filePath, diagnostics);
        _events.Publish(new DiagnosticsUpdatedEvent(filePath, diagnostics));
    }

    private static JsonObject BuildInitializeParams(string rootPath) => new()
    {
        ["processId"] = Environment.ProcessId,
        ["clientInfo"] = new JsonObject { ["name"] = "Fluence", ["version"] = "1.0" },
        ["rootUri"] = FilePathToUri(rootPath),
        ["capabilities"] = new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["completion"] = new JsonObject
                {
                    ["completionItem"] = new JsonObject
                    {
                        ["snippetSupport"] = true,
                        ["documentationFormat"] = new JsonArray { "plaintext" },
                    },
                },
                ["publishDiagnostics"] = new JsonObject { ["relatedInformation"] = false },
                ["definition"] = new JsonObject { ["linkSupport"] = false },
                ["implementation"] = new JsonObject { ["linkSupport"] = false },
                ["typeDefinition"] = new JsonObject { ["linkSupport"] = false },
                ["hover"] = new JsonObject
                {
                    ["contentFormat"] = new JsonArray { "plaintext", "markdown" },
                },
                ["signatureHelp"] = new JsonObject
                {
                    ["signatureInformation"] = new JsonObject
                    {
                        ["documentationFormat"] = new JsonArray { "plaintext" },
                        ["parameterInformation"] = new JsonObject { ["labelOffsetSupport"] = true },
                    },
                    ["contextSupport"] = true,
                },
                ["semanticTokens"] = new JsonObject
                {
                    ["requests"] = new JsonObject { ["full"] = true },
                    ["tokenTypes"] = new JsonArray
                    {
                        "namespace", "type", "class", "enum", "interface", "struct",
                        "typeParameter", "parameter", "variable", "property",
                        "enumMember", "event", "function", "method", "keyword",
                        "modifier", "comment", "string", "number", "operator",
                    },
                    ["tokenModifiers"] = new JsonArray
                    {
                        "declaration", "definition", "readonly", "static",
                        "abstract", "async", "modification", "documentation", "defaultLibrary",
                    },
                    ["formats"] = new JsonArray { "relative" },
                    ["overlappingTokenSupport"] = false,
                    ["multilineTokenSupport"] = true,
                },
            },
            ["workspace"] = new JsonObject
            {
                ["didChangeConfiguration"] = new JsonObject(),
            },
        },
    };

    private static string FilePathToUri(string path) =>
        new Uri(path).AbsoluteUri;

    private static string UriToFilePath(string uri) =>
        new Uri(uri).LocalPath;

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
