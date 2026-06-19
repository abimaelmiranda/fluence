using System.Text.Json.Serialization;

namespace Fluence.Modules.LanguageServer.Json;

[JsonSerializable(typeof(LanguageServerSettings))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
public sealed partial class LanguageServerSettingsJsonContext : JsonSerializerContext { }
