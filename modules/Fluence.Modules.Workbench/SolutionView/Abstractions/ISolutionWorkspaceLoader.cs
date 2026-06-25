using System.Threading;
using System.Threading.Tasks;
using Fluence.Modules.Workbench.SolutionView.Models;

namespace Fluence.Modules.Workbench.SolutionView.Abstractions;

public interface ISolutionWorkspaceLoader
{
    Task<SolutionWorkspaceSnapshot> LoadStructuralAsync(string solutionPath, CancellationToken cancellationToken = default);
    Task<SolutionTreeNode> LoadProjectAsync(string solutionPath, string projectPath, CancellationToken cancellationToken = default);
}
