using Fluence.Modules.NuGetExplorer.Abstractions;
using Fluence.Modules.NuGetExplorer.Models;
using NuGet.Common;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace Fluence.Modules.NuGetExplorer.Services;

public sealed class NuGetOrgPackageSource : INuGetPackageSource
{
    private static readonly SourceRepository Repository =
        NuGet.Protocol.Core.Types.Repository.Factory.GetCoreV3("https://api.nuget.org/v3/index.json");

    public async Task<IReadOnlyList<NuGetPackageSearchResult>> SearchAsync(
        string query,
        bool includePrerelease,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var resource = await Repository.GetResourceAsync<PackageSearchResource>(cancellationToken);
        var filter = new SearchFilter(includePrerelease)
        {
            IncludeDelisted = false,
        };

        var results = await resource.SearchAsync(query, filter, skip: 0, take: 50, NullLogger.Instance, cancellationToken);
        return results
            .Select(package => new NuGetPackageSearchResult(
                package.Identity.Id,
                package.Identity.Version.ToNormalizedString(),
                package.Description,
                package.Authors,
                package.IconUrl?.AbsoluteUri))
            .OrderBy(package => package.Id, System.StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<string>> GetVersionsAsync(
        string packageId,
        bool includePrerelease,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return [];
        }

        var resource = await Repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        var versions = await resource.GetAllVersionsAsync(
            packageId,
            new SourceCacheContext(),
            NullLogger.Instance,
            cancellationToken);

        return versions
            .Where(version => includePrerelease || !version.IsPrerelease)
            .OrderByDescending(version => version)
            .Select(version => version.ToNormalizedString())
            .ToArray();
    }

    public async Task<string?> GetLatestVersionAsync(
        string packageId,
        bool includePrerelease,
        CancellationToken cancellationToken = default)
    {
        var versions = await GetVersionsAsync(packageId, includePrerelease, cancellationToken);
        return versions.FirstOrDefault();
    }
}
