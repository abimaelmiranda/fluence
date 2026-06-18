using System.Text.Json.Serialization;

namespace Fluence.Modules.Debug.Json;

[JsonSerializable(typeof(DebugSettings))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
public sealed partial class DebugSettingsJsonContext : JsonSerializerContext { }
