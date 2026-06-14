using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Modules.SolutionView;

public interface ISolutionFileCreationDialogService
{
    Task<SolutionFileCreationRequest?> ShowCreateFileDialogAsync(CancellationToken cancellationToken = default);
}
