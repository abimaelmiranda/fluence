using System.Threading;
using System.Threading.Tasks;
using Fluence.Modules.Workbench.SolutionView.Models;

namespace Fluence.Modules.Workbench.SolutionView.Abstractions;

public interface ISolutionFileCreationDialogService
{
    Task<SolutionFileCreationRequest?> ShowCreateFileDialogAsync(CancellationToken cancellationToken = default);
}