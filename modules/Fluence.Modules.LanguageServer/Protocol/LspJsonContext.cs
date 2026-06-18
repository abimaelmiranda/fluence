using System.Text.Json.Serialization;

namespace Fluence.Modules.LanguageServer.Protocol;

[JsonSerializable(typeof(LspPublishDiagnosticsParamsRaw))]
[JsonSerializable(typeof(LspLocationRaw))]
[JsonSerializable(typeof(LspSemanticTokensRaw))]
[JsonSerializable(typeof(LspInitializeResultRaw))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
internal partial class LspJsonContext : JsonSerializerContext { }
