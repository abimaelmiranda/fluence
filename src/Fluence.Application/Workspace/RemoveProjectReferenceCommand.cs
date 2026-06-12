namespace Fluence.Application.Workspace;

public sealed record RemoveProjectReferenceCommand(
    string ProjectPath,
    string ReferencedProjectPath);
