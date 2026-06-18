namespace Fluence.Modules.NuGetExplorer.Models;

public sealed record NuGetInstalledPackage(
    string ProjectPath,
    string ProjectName,
    string Id,
    string Version);

