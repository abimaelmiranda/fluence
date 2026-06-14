namespace Fluence.Modules.SolutionView.Commands.RemoveProjectReference;

public sealed record RemoveProjectReferenceCommand(
    string ProjectPath,
    string ReferencedProjectPath);