namespace Fluence.Modules.Workbench.SolutionView.Models;

public sealed record SolutionWorkspaceSnapshot(
    string SolutionPath,
    SolutionTreeNode Root);