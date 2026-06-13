using System;
using System.IO;

namespace Fluence.Modules.DotnetCli;

internal static class DotnetPathHelpers
{
    public static bool IsProjectPath(string path)
    {
        return Path.GetExtension(path).EndsWith("proj", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRunnableFile(string? filePath)
    {
        return !string.IsNullOrWhiteSpace(filePath) &&
               File.Exists(filePath) &&
               string.Equals(Path.GetExtension(filePath), ".cs", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeDirectory(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    public static bool IsSameOrChildDirectory(string candidateDirectory, string rootDirectory)
    {
        return string.Equals(candidateDirectory, rootDirectory, StringComparison.OrdinalIgnoreCase) ||
               candidateDirectory.StartsWith(rootDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
