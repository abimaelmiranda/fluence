using System.Text.Json.Serialization;

namespace Fluence.Modules.SourceControl.Json;

[JsonSerializable(typeof(SourceControlSettings))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
public sealed partial class SourceControlSettingsJsonContext : JsonSerializerContext { }
