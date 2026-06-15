namespace Fluence.Modules.NuGetExplorer.Models;

public sealed record NuGetPackageUpdate(
    string Id,
    string LatestVersion,
    IReadOnlyList<NuGetInstalledPackage> InstalledPackages)
{
    public int ProjectCount => InstalledPackages.Count;

    public string ProjectCountLabel => ProjectCount == 1 ? "1 project" : $"{ProjectCount} projects";
}
