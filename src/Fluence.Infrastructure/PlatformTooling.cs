using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Fluence.Infrastructure;

internal abstract class PlatformTooling
{
    public static PlatformTooling Current { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new WindowsPlatformTooling()
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? new MacOsPlatformTooling()
                : new LinuxPlatformTooling();

    public abstract string DotnetExecutableName { get; }

    public abstract string NetcoredbgExecutableName { get; }

    public abstract string OmniSharpExecutableName { get; }

    public abstract string RuntimeId { get; }

    public abstract IReadOnlyList<string> PathExtensions { get; }

    public abstract string DefaultDotnetRoot { get; }

    public abstract void MakeExecutable(string executable);

    public string? FindOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in PathExtensions)
            {
                var candidate = Path.Combine(directory, executable + extension);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    protected static string ArchitectureId =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
}
