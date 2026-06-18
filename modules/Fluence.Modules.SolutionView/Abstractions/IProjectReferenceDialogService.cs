using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Modules.SolutionView.Models;

namespace Fluence.Modules.SolutionView.Abstractions;

public interface IProjectReferenceDialogService
{
    Task<IReadOnlyList<string>> ShowAddReferenceDialogAsync(
        IReadOnlyList<ProjectReferenceCandidate> candidates,
        CancellationToken cancellationToken = default);

    Task<bool> ConfirmRemoveProjectReferenceAsync(
        string referenceName,
        CancellationToken cancellationToken = default);
}