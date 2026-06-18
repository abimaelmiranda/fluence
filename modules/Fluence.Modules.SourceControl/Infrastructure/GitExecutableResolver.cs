using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Fluence.Modules.SourceControl.Infrastructure;

internal abstract class GitExecutableResolver
{
    public static GitExecutableResolver Current { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new WindowsGitExecutableResolver()
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? new MacOsGitExecutableResolver()
                : new LinuxGitExecutableResolver();

    public abstract string Resolve();

    protected static string? FindOnPath(string executable, IReadOnlyList<string> extensions)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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
}
