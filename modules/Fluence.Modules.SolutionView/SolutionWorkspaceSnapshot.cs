namespace Fluence.Modules.SolutionView;

public sealed record SolutionWorkspaceSnapshot(
    string SolutionPath,
    SolutionTreeNode Root);
