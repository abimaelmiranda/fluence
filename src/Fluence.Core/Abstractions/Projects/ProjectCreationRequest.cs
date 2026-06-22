using System.Collections.Generic;

namespace Fluence.Core.Abstractions.Projects;

public sealed record ProjectCreationRequest(
    string ProjectName,
    string Location,
    ProjectTemplateDefinition Template,
    string? Framework,
    string? SolutionName,
    bool CreateSolution,
    bool PlaceSolutionInProjectFolder,
    bool OpenAfterCreate,
    IReadOnlyDictionary<string, string> Options);
