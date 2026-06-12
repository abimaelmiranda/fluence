using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Modules.SolutionView;

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
