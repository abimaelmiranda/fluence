using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Models.LanguageServer;
using Fluence.Modules.LanguageServer;

namespace Fluence.Modules.LanguageServer.Services;

internal sealed class CodeActionService(
    ILanguageServerService lsp,
    LspClientHolder holder,
    ISettingsService settings) : ICodeActionService
{
    public async Task<LspCodeAction[]> GetCodeActionsAsync(
        string filePath,
        int startLine, int startChar,
        int endLine, int endChar,
        LspDiagnostic? diagnostic,
        CancellationToken cancellationToken = default)
    {
        if (!lsp.IsRunning || holder.Client is null)
            return [];

        try
        {
            var diagArr = new JsonArray();
            if (diagnostic is not null)
            {
                diagArr.Add(new JsonObject
                {
                    ["message"]  = diagnostic.Message,
                    ["severity"] = (int)diagnostic.Severity,
                    ["range"]    = new JsonObject
                    {
                        ["start"] = new JsonObject { ["line"] = diagnostic.StartLine, ["character"] = diagnostic.StartCharacter },
                        ["end"]   = new JsonObject { ["line"] = diagnostic.EndLine,   ["character"] = diagnostic.EndCharacter },
                    },
                    ["code"] = diagnostic.Code is not null ? JsonValue.Create(diagnostic.Code) : null,
                });
            }

            var result = await holder.Client.SendRequestAsync("textDocument/codeAction", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = new Uri(filePath).AbsoluteUri },
                ["range"] = new JsonObject
                {
                    ["start"] = new JsonObject { ["line"] = startLine, ["character"] = startChar },
                    ["end"]   = new JsonObject { ["line"] = endLine,   ["character"] = endChar },
                },
                ["context"] = new JsonObject
                {
                    ["diagnostics"] = diagArr,
                },
            }, cancellationToken).ConfigureAwait(false);

            var suppressedCodes = SuppressedDiagnosticCodes.From(settings.Get<LanguageServerSettings>());
            return ParseCodeActions(result, suppressedCodes);
        }
        catch
        {
            return [];
        }
    }

    public async Task<LspCodeAction?> ResolveAsync(LspCodeAction action, CancellationToken cancellationToken = default)
    {
        if (!lsp.IsRunning || holder.Client is null || action.RawJson is null)
            return null;

        try
        {
            var rawNode = JsonNode.Parse(action.RawJson);
            if (rawNode is null) return null;

            System.Diagnostics.Debug.WriteLine($"[LSP/CodeAction] codeAction/resolve → {action.Title}");
            var result = await holder.Client.SendRequestAsync("codeAction/resolve", rawNode, cancellationToken).ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[LSP/CodeAction] codeAction/resolve ← {result?.ToJsonString()}");

            if (result is not JsonObject resolved) return null;

            LspWorkspaceEdit? edit = null;
            if (resolved["edit"] is JsonObject editObj)
                edit = ParseWorkspaceEdit(editObj);

            string? commandId = null;
            string? argsJson  = null;
            if (resolved["command"] is JsonValue cmdVal)
            {
                try { commandId = cmdVal.GetValue<string>(); } catch { }
                argsJson = resolved["arguments"]?.ToJsonString();
            }
            else if (resolved["command"] is JsonObject cmdObj)
            {
                commandId = cmdObj["command"]?.GetValue<string>();
                argsJson  = cmdObj["arguments"]?.ToJsonString();
            }

            return new LspCodeAction(action.Title, action.IsPreferred, edit, commandId, argsJson, action.RawJson);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LSP/CodeAction] codeAction/resolve failed: {ex.Message}");
            return null;
        }
    }

    public async Task ExecuteCommandAsync(string command, string? argumentsJson, CancellationToken cancellationToken = default)
    {
        if (!lsp.IsRunning || holder.Client is null)
            return;

        try
        {
            var args = new JsonObject { ["command"] = command };
            if (argumentsJson is not null)
            {
                var parsed = JsonNode.Parse(argumentsJson);
                if (parsed is not null)
                    args["arguments"] = parsed;
            }

            System.Diagnostics.Debug.WriteLine($"[LSP/CodeAction] workspace/executeCommand → {command}");
            await holder.Client.SendRequestAsync("workspace/executeCommand", args, cancellationToken).ConfigureAwait(false);
            System.Diagnostics.Debug.WriteLine($"[LSP/CodeAction] workspace/executeCommand ← done");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LSP/CodeAction] workspace/executeCommand failed: {ex.Message}");
        }
    }

    private static LspCodeAction[] ParseCodeActions(JsonNode? result, IReadOnlySet<string> suppressedCodes)
    {
        if (result is not JsonArray arr)
            return [];

        var actions = new List<LspCodeAction>(arr.Count);
        foreach (var item in arr)
        {
            if (item is not JsonObject obj) continue;

            var title = obj["title"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(title)) continue;
            if (IsSuppressedCodeAction(obj, title, suppressedCodes)) continue;

            var isPreferred = obj["isPreferred"]?.GetValue<bool>() ?? false;

            LspWorkspaceEdit? edit = null;
            if (obj["edit"] is JsonObject editObj)
                edit = ParseWorkspaceEdit(editObj);

            string? commandId = null;
            string? argsJson  = null;

            var commandNode = obj["command"];
            if (commandNode is JsonValue cmdVal)
            {
                // Item is a Command: { title, command (string), arguments }
                try { commandId = cmdVal.GetValue<string>(); } catch { }
                argsJson = obj["arguments"]?.ToJsonString();
            }
            else if (commandNode is JsonObject cmdObj)
            {
                // Item is a CodeAction with embedded Command object
                commandId = cmdObj["command"]?.GetValue<string>();
                argsJson  = cmdObj["arguments"]?.ToJsonString();
            }

            var rawJson = obj.ToJsonString();
            System.Diagnostics.Debug.WriteLine($"[LSP/CodeAction] parsed → title='{title}' cmd='{commandId}' hasEdit={edit is not null}");
            actions.Add(new LspCodeAction(title, isPreferred, edit, commandId, argsJson, rawJson));
        }

        return [.. actions];
    }

    private static bool IsSuppressedCodeAction(
        JsonObject obj,
        string title,
        IReadOnlySet<string> suppressedCodes)
    {
        if (suppressedCodes.Count == 0)
            return false;

        foreach (var code in ExtractDiagnosticCodes(obj))
        {
            if (SuppressedDiagnosticCodes.Contains(suppressedCodes, code))
                return true;
        }

        return suppressedCodes.Any(code => title.Contains(code, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> ExtractDiagnosticCodes(JsonObject obj)
    {
        if (obj["diagnostics"] is not JsonArray diagnostics)
            yield break;

        foreach (var diagnostic in diagnostics)
        {
            var code = diagnostic?["code"];
            if (code is null)
                continue;

            string? text;
            try
            {
                text = code switch
                {
                    JsonValue value => value.GetValueKind() switch
                    {
                        System.Text.Json.JsonValueKind.String => value.GetValue<string>(),
                        System.Text.Json.JsonValueKind.Number => value.GetValue<int>().ToString(),
                        _ => null,
                    },
                    JsonObject objectCode => objectCode["value"]?.GetValue<string>(),
                    _ => null,
                };
            }
            catch
            {
                text = null;
            }

            if (!string.IsNullOrWhiteSpace(text))
                yield return text;
        }
    }

    private static LspWorkspaceEdit? ParseWorkspaceEdit(JsonObject editObj)
    {
        if (editObj["changes"] is not JsonObject changesObj)
            return null;

        var changes = new Dictionary<string, LspTextEdit[]>();
        foreach (var (uri, editsNode) in changesObj)
        {
            if (editsNode is not JsonArray editsArr) continue;

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

            changes[uri] = [.. edits];
        }

        return new LspWorkspaceEdit(changes);
    }
}
