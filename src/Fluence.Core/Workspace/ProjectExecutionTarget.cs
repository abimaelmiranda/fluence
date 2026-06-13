namespace Fluence.Core.Workspace;

public sealed record ProjectExecutionTarget(
    string ProjectPath,
    ProjectExecutionTargetKind Kind,
    LaunchConfiguration? Configuration = null);
