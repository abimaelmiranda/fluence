using System;
using System.IO;
using System.Linq;
using Fluence.Infrastructure;

namespace Fluence.Infrastructure.Protocols.Lsp;

public sealed partial class OmniSharpProvisioningService
{
    private static string ResolveExecutableName() => PlatformTooling.Current.OmniSharpExecutableName;

    private static string ResolveDotnetDir()
    {
        var dotnet = ResolveDotnetExecutable();
        if (dotnet is not null)
            return Path.GetDirectoryName(dotnet)!;

        return PlatformTooling.Current.DefaultDotnetRoot;
    }

    private static string? ResolveDotnetExecutable()
    {
        var dotnet = FindOnPath("dotnet");
        return dotnet is null ? null : ResolveRealPath(dotnet);
    }

    private static string? ResolveSelectedSdkPath()
    {
        var sdkRoot = Path.Combine(ResolveDotnetDir(), "sdk");
        if (!Directory.Exists(sdkRoot))
            return null;

        return Directory
            .EnumerateDirectories(sdkRoot)
            .Select(path => new
            {
                Path = path,
                Version = ParseSdkVersion(Path.GetFileName(path)),
            })
            .OrderByDescending(item => item.Version)
            .ThenByDescending(item => Path.GetFileName(item.Path), StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Path)
            .FirstOrDefault();
    }

    private static Version ParseSdkVersion(string? directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
            return new Version(0, 0);

        var stablePart = directoryName.Split('-', 2)[0];
        return Version.TryParse(stablePart, out var version) ? version : new Version(0, 0);
    }

    private static string ResolveRealPath(string path)
    {
        try
        {
            var file = new FileInfo(path);
            var target = file.ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
                return target.FullName;
        }
        catch
        {
        }

        return Path.GetFullPath(path);
    }

    private static string? FindOnPath(string executable)
    {
        return PlatformTooling.Current.FindOnPath(executable);
    }

    private static string ResolveRuntimeId() => PlatformTooling.Current.RuntimeId;
}
