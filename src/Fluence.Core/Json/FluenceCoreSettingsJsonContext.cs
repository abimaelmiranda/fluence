using System.Text.Json.Serialization;
using Fluence.Core.Models.Settings;

namespace Fluence.Core.Json;

[JsonSerializable(typeof(GlobalSettings))]
[JsonSerializable(typeof(ShellSettings))]
[JsonSerializable(typeof(ProblemsSettings))]
[JsonSerializable(typeof(DiagnosticsSettings))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
public sealed partial class FluenceCoreSettingsJsonContext : JsonSerializerContext { }
