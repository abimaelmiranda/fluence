namespace Fluence.Modules.NuGetExplorer.Models;

public sealed record NuGetPackageSearchResult(
    string Id,
    string Version,
    string? Description,
    string? Authors,
    string? IconUrl);