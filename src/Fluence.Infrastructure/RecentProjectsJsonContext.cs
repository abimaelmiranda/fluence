using System.Text.Json.Serialization;
using Fluence.Core.Models.Workspace;

namespace Fluence.Infrastructure;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RecentProjectsData))]
internal sealed partial class RecentProjectsJsonContext : JsonSerializerContext;
