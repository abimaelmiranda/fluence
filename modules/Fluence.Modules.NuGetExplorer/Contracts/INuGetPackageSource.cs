using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Modules.NuGetExplorer;

public interface INuGetPackageSource
{
    Task<IReadOnlyList<NuGetPackageSearchResult>> SearchAsync(
        string query,
        bool includePrerelease,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        bool includePrerelease,
        CancellationToken cancellationToken = default);

    Task<string?> GetLatestVersionAsync(
        string packageId,
        bool includePrerelease,
        CancellationToken cancellationToken = default);
}
