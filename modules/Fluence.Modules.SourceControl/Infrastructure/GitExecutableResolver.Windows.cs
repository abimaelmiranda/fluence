using System;
using System.Collections.Generic;
using System.IO;

namespace Fluence.Modules.SourceControl.Infrastructure;

internal sealed class WindowsGitExecutableResolver : GitExecutableResolver
{
    private static readonly string[] PathExtensions = [".exe", ".cmd", ".bat", string.Empty];

    public override string Resolve()
    {
        var onPath = FindOnPath("git", PathExtensions);
        if (onPath is not null)
            return onPath;

        foreach (var candidate in GetKnownInstallPaths())
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return "git.exe";
    }

    private static IEnumerable<string> GetKnownInstallPaths()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (!string.IsNullOrWhiteSpace(programFiles))
            yield return Path.Combine(programFiles, "Git", "cmd", "git.exe");
        if (!string.IsNullOrWhiteSpace(programFilesX86))
            yield return Path.Combine(programFilesX86, "Git", "cmd", "git.exe");
        if (!string.IsNullOrWhiteSpace(localAppData))
            yield return Path.Combine(localAppData, "Programs", "Git", "cmd", "git.exe");
    }
}
