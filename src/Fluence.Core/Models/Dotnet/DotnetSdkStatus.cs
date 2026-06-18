using System.Collections.Generic;

namespace Fluence.Core.Models.Dotnet;

public sealed record DotnetSdkStatus(
    bool IsDotnetAvailable,
    string? DotnetPath,
    string IdeManagedInstallPath,
    string RecommendedVersion,
    IReadOnlyList<DotnetSdkInfo> InstalledSdks,
    string? ErrorMessage)
{
    public bool HasRecommendedSdk => InstalledSdks.Count > 0;
}
