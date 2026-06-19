using System.Threading;
using System.Threading.Tasks;
using Fluence.Modules.SolutionView.Models;

namespace Fluence.Modules.SolutionView.Abstractions;

public interface ISolutionWorkspaceLoader
{
    Task<SolutionWorkspaceSnapshot> LoadStructuralAsync(string solutionPath, CancellationToken cancellationToken = default);
    Task<SolutionTreeNode> LoadProjectAsync(string solutionPath, string projectPath, CancellationToken cancellationToken = default);
}
