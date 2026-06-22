using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Models.LanguageServer;
using Fluence.Modules.LanguageServer.Protocol;
using Fluence.Core.Events.Document;
using Fluence.Core.Events.Lsp;
using Fluence.Modules.LanguageServer;

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
        WriteOutput($"[LanguageServer] {_serverDisplayName} disconnected\r\n", OutputChannelEntryKind.Warning);
        var client = _client;
        _client = null;
        _holder.Client = null;
        if (client is not null)
            client.NotificationReceived -= OnNotificationReceived;
    }

    private void OnNotificationReceived(string method, JsonNode? parameters, int? requestId)
    {
        if (method == "textDocument/publishDiagnostics")
            HandlePublishDiagnostics(parameters);
        else if (method == "workspace/applyEdit" && requestId.HasValue)
            HandleApplyEdit(parameters, requestId.Value);
    }

    private void HandleApplyEdit(JsonNode? parameters, int requestId)
    {
        try
        {
            Debug.WriteLine($"[LS] workspace/applyEdit received, requestId={requestId}");

            if (parameters?["edit"] is not JsonObject editObj)
            {
                Debug.WriteLine("[LS] workspace/applyEdit: missing 'edit' node");
                SafeSendApplyEditResponse(requestId, applied: false);
                return;
            }

            // LSP spec allows both "changes" (dict) and "documentChanges" (array) formats.
            // Roslyn-style servers typically use documentChanges.
            if (editObj["changes"] is JsonObject changesObj)
            {
                foreach (var (uri, editsNode) in changesObj)
                {
                    if (editsNode is JsonArray arr)
                        PublishEditsForFile(UriToFilePath(uri), arr);
                }
            }
            else if (editObj["documentChanges"] is JsonArray docChanges)
            {
                foreach (var change in docChanges)
                {
                    if (change is not JsonObject changeObj) continue;
                    var uri      = changeObj["textDocument"]?["uri"]?.GetValue<string>();
                    var editsArr = changeObj["edits"] as JsonArray;
                    if (uri is not null && editsArr is not null)
                        PublishEditsForFile(UriToFilePath(uri), editsArr);
                }
            }
            else
            {
                Debug.WriteLine($"[LS] workspace/applyEdit: neither 'changes' nor 'documentChanges' found. edit keys: {string.Join(", ", editObj.Select(kv => kv.Key))}");
                SafeSendApplyEditResponse(requestId, applied: false);
                return;
            }

            SafeSendApplyEditResponse(requestId, applied: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LS] HandleApplyEdit failed: {ex.Message}");
            SafeSendApplyEditResponse(requestId, applied: false);
        }
    }

    private void PublishEditsForFile(string filePath, JsonArray editsArr)
    {
        var edits = new List<LspTextEdit>(editsArr.Count);
        foreach (var e in editsArr)
        {
            if (e is not JsonObject editItem) continue;
            var newText = editItem["newText"]?.GetValue<string>() ?? string.Empty;
            if (editItem["range"] is not JsonObject range) continue;
            var start = range["start"] as JsonObject;
            var end   = range["end"]   as JsonObject;
            if (start is null || end is null) continue;
            edits.Add(new LspTextEdit(
                newText,
                start["line"]?.GetValue<int>() ?? 0,
                start["character"]?.GetValue<int>() ?? 0,
                end["line"]?.GetValue<int>() ?? 0,
                end["character"]?.GetValue<int>() ?? 0));
        }
        Debug.WriteLine($"[LS] workspace/applyEdit: publishing {edits.Count} edits for {filePath}");
        _events.Publish(new WorkspaceEditRequestedEvent(filePath, edits));
    }

    private void SafeSendApplyEditResponse(int requestId, bool applied)
    {
        var client = _holder.Client;
        if (client is null) return;
        _ = client.SendResponseAsync(requestId, new JsonObject { ["applied"] = applied }, CancellationToken.None);
    }

    private void HandlePublishDiagnostics(JsonNode? parameters)
    {
        var msg = parameters?.Deserialize(LspJsonContext.Default.LspPublishDiagnosticsParamsRaw);
        if (msg is null) return;

        var filePath = UriToFilePath(msg.Uri);
        var suppressedCodes = SuppressedDiagnosticCodes.From(_settings.Get<LanguageServerSettings>());
        var diagnostics = new List<LspDiagnostic>(msg.Diagnostics.Length);

        foreach (var d in msg.Diagnostics)
        {
            var code = d.Code?.ValueKind switch
            {
                JsonValueKind.String => d.Code.Value.GetString(),
                JsonValueKind.Number => d.Code.Value.GetInt32().ToString(),
                _ => null,
            };

            if (SuppressedDiagnosticCodes.Contains(suppressedCodes, code))
                continue;

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
