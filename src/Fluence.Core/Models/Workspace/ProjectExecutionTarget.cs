using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Core.Models.Workspace;

public sealed record ProjectExecutionTarget(
    string ProjectPath,
    ProjectExecutionTargetKind Kind,
    LaunchConfiguration? Configuration = null);
