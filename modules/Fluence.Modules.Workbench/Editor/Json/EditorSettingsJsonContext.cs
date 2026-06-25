using System.Text.Json.Serialization;

namespace Fluence.Modules.Workbench.Editor.Json;

[JsonSerializable(typeof(EditorSettings))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
public sealed partial class EditorSettingsJsonContext : JsonSerializerContext { }
