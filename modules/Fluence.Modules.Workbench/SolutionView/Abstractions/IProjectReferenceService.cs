using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Modules.Workbench.SolutionView.Models;

namespace Fluence.Modules.Workbench.SolutionView.Abstractions;

public interface IProjectReferenceService
{
    Task<IReadOnlyList<ProjectReferenceCandidate>> GetReferenceCandidatesAsync(
        string projectPath,
        SolutionWorkspaceSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task AddProjectReferencesAsync(
        string projectPath,
        IReadOnlyList<string> referencedProjectPaths,
        CancellationToken cancellationToken = default);

    Task RemoveProjectReferenceAsync(
        string projectPath,
        string referencedProjectPath,
        CancellationToken cancellationToken = default);
}