using System.Text.Json.Serialization;
using Fluence.Core.Models.Settings;

namespace Fluence.Core.Json;

[JsonSerializable(typeof(GlobalSettings))]
[JsonSerializable(typeof(ShellSettings))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
public sealed partial class FluenceCoreSettingsJsonContext : JsonSerializerContext { }
