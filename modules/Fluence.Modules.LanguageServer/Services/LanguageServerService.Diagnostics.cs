using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.LanguageServer;
using Fluence.Modules.LanguageServer.Protocol;

namespace Fluence.Modules.LanguageServer.Services;

internal sealed partial class LanguageServerService
{
    private void CaptureSemanticTokenLegend(JsonNode? initResult)
    {
        try
        {
            var result = initResult?.Deserialize(LspJsonContext.Default.LspInitializeResultRaw);
            var legend = result?.Capabilities?.SemanticTokensProvider?.Legend;
            if (legend is null) return;

            SemanticTokenTypes = legend.TokenTypes;
            SemanticTokenModifiers = legend.TokenModifiers;
        }
        catch (Exception ex) { Debug.WriteLine($"[LS] CaptureSemanticTokenLegend failed: {ex.Message}"); }
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
        var msg = parameters?.Deserialize(LspJsonContext.Default.LspPublishDiagnosticsParamsRaw);
        if (msg is null) return;

        var filePath = UriToFilePath(msg.Uri);
        var diagnostics = new List<LspDiagnostic>(msg.Diagnostics.Length);

        foreach (var d in msg.Diagnostics)
        {
            var code = d.Code?.ValueKind switch
            {
                JsonValueKind.String => d.Code.Value.GetString(),
                JsonValueKind.Number => d.Code.Value.GetInt32().ToString(),
                _ => null,
            };

            diagnostics.Add(new LspDiagnostic(
                Message: d.Message,
                Severity: (LspDiagnosticSeverity)(d.Severity ?? 1),
                StartLine: d.Range.Start.Line,
                StartCharacter: d.Range.Start.Character,
                EndLine: d.Range.End.Line,
                EndCharacter: d.Range.End.Character,
                Code: code));
        }

        _diagnostics.UpdateDiagnostics(filePath, diagnostics);
        _events.Publish(new DiagnosticsUpdatedEvent(filePath, diagnostics));
    }
}
