using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Modules.SolutionView;

public interface IProjectReferenceDialogService
{
    Task<IReadOnlyList<string>> ShowAddReferenceDialogAsync(
        IReadOnlyList<ProjectReferenceCandidate> candidates,
        CancellationToken cancellationToken = default);

    Task<bool> ConfirmRemoveProjectReferenceAsync(
        string referenceName,
        CancellationToken cancellationToken = default);
}
