namespace Fluence.Modules.SolutionView;

public sealed record RemoveProjectReferenceCommand(
    string ProjectPath,
    string ReferencedProjectPath);
