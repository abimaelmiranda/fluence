using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Abstractions.Projects;

public interface IProjectTemplateProvider
{
    string ProviderId { get; }

    string DisplayName { get; }

    bool SupportsSolutionCreation { get; }

    IReadOnlyList<ProjectTemplateDefinition> Templates { get; }

    bool CanHandleWorkspace(string? languageId);

    Task<ProjectCreationResult> CreateAsync(
        ProjectCreationRequest request,
        Action<string> onOutput,
        CancellationToken cancellationToken = default);
}
