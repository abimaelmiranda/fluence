using System.Text.Json.Serialization;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;

namespace Fluence.Infrastructure;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(LaunchSettings))]
internal sealed partial class LaunchSettingsJsonContext : JsonSerializerContext;
