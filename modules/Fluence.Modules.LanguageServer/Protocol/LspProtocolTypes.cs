using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fluence.Modules.LanguageServer.Protocol;

// ─── Diagnostics ────────────────────────────────────────────────────────────

internal record LspPublishDiagnosticsParamsRaw(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("diagnostics")] LspDiagnosticRaw[] Diagnostics);

internal record LspDiagnosticRaw(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("severity")] int? Severity,
    [property: JsonPropertyName("range")] LspRangeRaw Range,
    // LSP spec allows string | int; keep as JsonElement to handle both without reflection fallback.
    [property: JsonPropertyName("code")] JsonElement? Code);

// ─── Navigation ─────────────────────────────────────────────────────────────

internal record LspLocationRaw(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("range")] LspRangeRaw Range);

// ─── Shared geometry ────────────────────────────────────────────────────────

internal record LspRangeRaw(
    [property: JsonPropertyName("start")] LspPositionRaw Start,
    [property: JsonPropertyName("end")] LspPositionRaw End);

internal record LspPositionRaw(
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("character")] int Character);

// ─── Semantic tokens ────────────────────────────────────────────────────────

internal record LspSemanticTokensRaw(
    [property: JsonPropertyName("data")] int[] Data);

// ─── Initialize result (minimal — only the semantic token legend is consumed) ─

internal record LspInitializeResultRaw(
    [property: JsonPropertyName("capabilities")] LspServerCapabilitiesRaw? Capabilities);

internal record LspServerCapabilitiesRaw(
    [property: JsonPropertyName("semanticTokensProvider")] LspSemanticTokensProviderRaw? SemanticTokensProvider);

internal record LspSemanticTokensProviderRaw(
    [property: JsonPropertyName("legend")] LspSemanticTokensLegendRaw? Legend);

internal record LspSemanticTokensLegendRaw(
    [property: JsonPropertyName("tokenTypes")] string[] TokenTypes,
    [property: JsonPropertyName("tokenModifiers")] string[] TokenModifiers);
