using System.Threading;
using System.Threading.Tasks;
using Fluence.Modules.SolutionView.Models;

namespace Fluence.Modules.SolutionView.Abstractions;

public interface ISolutionWorkspaceLoader
{
    Task<SolutionWorkspaceSnapshot> LoadAsync(string solutionPath, CancellationToken cancellationToken = default);
}