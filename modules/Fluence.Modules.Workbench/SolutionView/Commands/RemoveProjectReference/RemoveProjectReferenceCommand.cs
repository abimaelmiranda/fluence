namespace Fluence.Modules.Workbench.SolutionView.Commands.RemoveProjectReference;

public sealed record RemoveProjectReferenceCommand(
    string ProjectPath,
    string ReferencedProjectPath);