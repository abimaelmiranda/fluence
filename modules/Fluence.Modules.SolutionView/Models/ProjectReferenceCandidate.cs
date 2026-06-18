namespace Fluence.Modules.SolutionView.Models;

public sealed record ProjectReferenceCandidate(
    string Name,
    string ProjectPath,
    bool IsReferenced);