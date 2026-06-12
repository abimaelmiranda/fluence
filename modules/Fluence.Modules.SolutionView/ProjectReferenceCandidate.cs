namespace Fluence.Modules.SolutionView;

public sealed record ProjectReferenceCandidate(
    string Name,
    string ProjectPath,
    bool IsReferenced);
