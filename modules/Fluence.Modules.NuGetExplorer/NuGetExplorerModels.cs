using System.Collections.Generic;

namespace Fluence.Modules.NuGetExplorer;

public sealed record NuGetProject(
    string ProjectPath,
    string Name);

public sealed record NuGetPackageSearchResult(
    string Id,
    string Version,
    string? Description,
    string? Authors,
    string? IconUrl);

public sealed record NuGetInstalledPackage(
    string ProjectPath,
    string ProjectName,
    string Id,
    string Version);

public sealed record NuGetPackageUpdate(
    string Id,
    string LatestVersion,
    IReadOnlyList<NuGetInstalledPackage> InstalledPackages)
{
    public int ProjectCount => InstalledPackages.Count;

    public string ProjectCountLabel => ProjectCount == 1 ? "1 project" : $"{ProjectCount} projects";
}
