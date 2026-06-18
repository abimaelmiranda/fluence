namespace Fluence.Modules.SolutionView.Models;

public sealed record SolutionWorkspaceSnapshot(
    string SolutionPath,
    SolutionTreeNode Root);