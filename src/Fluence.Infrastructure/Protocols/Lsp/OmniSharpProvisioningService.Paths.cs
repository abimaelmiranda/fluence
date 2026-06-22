using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Fluence.Infrastructure;
using Fluence.Infrastructure.Languages;

namespace Fluence.Infrastructure.Protocols.Lsp;

public sealed partial class OmniSharpProvisioningService
{
    private static string ResolveExecutableName() => DotnetPlatformConstants.OmniSharpExecutableName;

    private static string ResolveDotnetDir()
    {
        var dotnet = ResolveDotnetExecutable();
        if (dotnet is not null)
            return Path.GetDirectoryName(dotnet)!;

        return DotnetPlatformConstants.DefaultDotnetRoot;
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

    private static string? ResolveSelectedSdkPathForRoot(string rootPath)
    {
        try
        {
            var dotnet = FindOnPath("dotnet");
            if (dotnet is null)
                return ResolveSelectedSdkPath();

            var psi = new ProcessStartInfo(dotnet, "--version")
            {
                WorkingDirectory = rootPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null)
                return ResolveSelectedSdkPath();

            var version = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(3000);

            if (string.IsNullOrWhiteSpace(version))
                return ResolveSelectedSdkPath();

            var sdkRoot = Path.Combine(ResolveDotnetDir(), "sdk");
            if (!Directory.Exists(sdkRoot))
                return null;

            var match = Directory.EnumerateDirectories(sdkRoot)
                .FirstOrDefault(d => Path.GetFileName(d).StartsWith(version, StringComparison.OrdinalIgnoreCase));

            return match ?? ResolveSelectedSdkPath();
        }
        catch
        {
            return ResolveSelectedSdkPath();
        }
    }

    private static string ResolveRuntimeId() => PlatformTooling.Current.RuntimeId;
}
