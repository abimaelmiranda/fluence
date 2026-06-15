using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Modules.NuGetExplorer.Models;

namespace Fluence.Modules.NuGetExplorer.Abstractions;

public interface INuGetProjectService
{
    bool UsesCentralPackageManagement(string solutionPath);

    Task<IReadOnlyList<NuGetProject>> GetProjectsAsync(
        string solutionPath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<NuGetInstalledPackage>> GetInstalledPackagesAsync(
        string solutionPath,
        CancellationToken cancellationToken = default);

    Task InstallPackageAsync(
        string projectPath,
        string packageId,
        string version,
        CancellationToken cancellationToken = default);

    Task RemovePackageAsync(
        string projectPath,
        string packageId,
        CancellationToken cancellationToken = default);
}
