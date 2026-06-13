using System.Text.Json.Serialization;
using Fluence.Core.Workspace;

namespace Fluence.Infrastructure;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LaunchSettings))]
internal sealed partial class LaunchSettingsJsonContext : JsonSerializerContext;
