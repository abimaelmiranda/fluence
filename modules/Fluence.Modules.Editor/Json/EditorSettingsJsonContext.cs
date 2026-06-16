using System.Text.Json.Serialization;

namespace Fluence.Modules.Editor.Json;

[JsonSerializable(typeof(EditorSettings))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
public sealed partial class EditorSettingsJsonContext : JsonSerializerContext { }
