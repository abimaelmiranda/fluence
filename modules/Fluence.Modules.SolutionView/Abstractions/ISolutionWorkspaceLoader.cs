using System.Threading;
using System.Threading.Tasks;
using Fluence.Modules.SolutionView.Models;

namespace Fluence.Modules.SolutionView.Abstractions;

public interface ISolutionWorkspaceLoader
{
    Task<SolutionWorkspaceSnapshot> LoadStructuralAsync(string solutionPath, CancellationToken cancellationToken = default);
    Task<SolutionWorkspaceSnapshot> LoadAsync(string solutionPath, CancellationToken cancellationToken = default);
    bool HasValidCache(string solutionPath);
}