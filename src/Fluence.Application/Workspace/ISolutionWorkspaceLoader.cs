using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Application.Workspace;

public interface ISolutionWorkspaceLoader
{
    Task<SolutionWorkspaceSnapshot> LoadAsync(string solutionPath, CancellationToken cancellationToken = default);
}
