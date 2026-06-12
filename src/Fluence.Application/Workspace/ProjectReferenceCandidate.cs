namespace Fluence.Application.Workspace;

public sealed record ProjectReferenceCandidate(
    string ProjectPath,
    string Name,
    bool IsReferenced);
