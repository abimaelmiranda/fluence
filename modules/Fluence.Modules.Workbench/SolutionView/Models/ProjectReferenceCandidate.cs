namespace Fluence.Modules.Workbench.SolutionView.Models;

public sealed record ProjectReferenceCandidate(
    string Name,
    string ProjectPath,
    bool IsReferenced);