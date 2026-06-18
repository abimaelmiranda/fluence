using System.Text.Json.Serialization;
using Fluence.Core.Models.Workspace;

namespace Fluence.Infrastructure;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WorkspaceSnapshot))]
internal sealed partial class WorkspaceSnapshotJsonContext : JsonSerializerContext;
