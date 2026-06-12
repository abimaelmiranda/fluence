using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Modules.SolutionView;

public interface ISolutionWorkspaceLoader
{
    Task<SolutionWorkspaceSnapshot> LoadAsync(string solutionPath, CancellationToken cancellationToken = default);
}
