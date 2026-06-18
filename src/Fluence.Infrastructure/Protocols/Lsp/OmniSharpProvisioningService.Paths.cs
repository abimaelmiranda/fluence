using System.IO;
using Fluence.Infrastructure;

namespace Fluence.Infrastructure.Protocols.Lsp;

public sealed partial class OmniSharpProvisioningService
{
    private static string ResolveExecutableName() => PlatformTooling.Current.OmniSharpExecutableName;

    private static string ResolveDotnetDir()
    {
        var dotnet = FindOnPath("dotnet");
        if (dotnet is not null)
            return Path.GetDirectoryName(dotnet)!;

        return PlatformTooling.Current.DefaultDotnetRoot;
    }

    private static string? FindOnPath(string executable)
    {
        return PlatformTooling.Current.FindOnPath(executable);
    }

    private static string ResolveRuntimeId() => PlatformTooling.Current.RuntimeId;
}
