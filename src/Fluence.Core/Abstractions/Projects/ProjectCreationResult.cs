namespace Fluence.Core.Abstractions.Projects;

public sealed record ProjectCreationResult(
    string ProjectRoot,
    string? SolutionPath);
