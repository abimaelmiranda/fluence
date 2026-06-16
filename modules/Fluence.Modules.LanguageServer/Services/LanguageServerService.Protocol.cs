using System;
using System.Text.Json.Nodes;

namespace Fluence.Modules.LanguageServer.Services;

internal sealed partial class LanguageServerService
{
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
}
