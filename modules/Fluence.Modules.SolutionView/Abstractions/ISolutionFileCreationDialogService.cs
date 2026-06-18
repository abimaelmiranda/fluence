using System.Threading;
using System.Threading.Tasks;
using Fluence.Modules.SolutionView.Models;

namespace Fluence.Modules.SolutionView.Abstractions;

public interface ISolutionFileCreationDialogService
{
    Task<SolutionFileCreationRequest?> ShowCreateFileDialogAsync(CancellationToken cancellationToken = default);
}