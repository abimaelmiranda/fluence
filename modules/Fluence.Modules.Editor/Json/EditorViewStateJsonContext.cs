using System.Text.Json.Serialization;
using Fluence.Modules.Editor.Services;

namespace Fluence.Modules.Editor.Json;

[JsonSerializable(typeof(EditorViewStateCache))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
public sealed partial class EditorViewStateJsonContext : JsonSerializerContext { }
