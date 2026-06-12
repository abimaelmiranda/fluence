namespace Fluence.Application.Workspace;

public sealed record SolutionWorkspaceSnapshot(
    string SolutionPath,
    SolutionTreeNode Root);
