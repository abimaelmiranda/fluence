using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Fluence.Infrastructure.Protocols.Lsp;

public sealed partial class OmniSharpProvisioningService
{
    private static string ResolveExecutableName() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "OmniSharp.exe" : "OmniSharp";

    private static string ResolveDotnetDir()
    {
        var dotnet = FindOnPath("dotnet");
        if (dotnet is not null)
            return Path.GetDirectoryName(dotnet)!;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return "/usr/local/share/dotnet";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet");

        return "/usr/share/dotnet";
    }

    private static string? FindOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var separator = Path.PathSeparator;
        var extensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { ".exe", ".cmd", ".bat", string.Empty }
            : new[] { string.Empty };

        foreach (var directory in path.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, executable + extension);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static string ResolveRuntimeId()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
            : "linux";
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        return $"{os}-{arch}";
    }
}
