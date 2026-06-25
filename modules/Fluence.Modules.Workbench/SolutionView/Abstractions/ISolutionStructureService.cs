using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Modules.Workbench.SolutionView.Abstractions;

public interface ISolutionStructureService
{
    Task CreatePhysicalFolderAsync(
        string projectPath,
        string parentDirectory,
        string folderName,
        CancellationToken cancellationToken = default);

    Task CreateSolutionFolderAsync(
        string solutionPath,
        string virtualParentPath,
        string folderName,
        CancellationToken cancellationToken = default);

    Task RemovePhysicalFolderAsync(
        string projectPath,
        string folderPath,
        CancellationToken cancellationToken = default);
}