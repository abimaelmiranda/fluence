using System.Text.Json.Serialization;
using Fluence.Modules.Workbench.Editor.Services;

namespace Fluence.Modules.Workbench.Editor.Json;

[JsonSerializable(typeof(EditorViewStateCache))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
public sealed partial class EditorViewStateJsonContext : JsonSerializerContext { }
