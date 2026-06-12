using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Ports;

public interface IWorkspaceDialogService
{
    Task<string?> PickFileAsync(CancellationToken cancellationToken = default);

    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);

    Task<string?> PickSolutionAsync(CancellationToken cancellationToken = default);
}
