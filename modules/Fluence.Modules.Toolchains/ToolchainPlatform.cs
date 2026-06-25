using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Fluence.Modules.Toolchains;

internal static class ToolchainPlatform
{
    private static readonly string[] PathExtensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? [".exe", ".cmd", ".bat", string.Empty]
        : [string.Empty];

    public static string? FindOnPath(string executable)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        foreach (var extension in PathExtensions)
        {
            var candidate = Path.Combine(directory, executable + extension);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public static void MakeExecutable(string executable)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        RunProcess("chmod", "+x", executable);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            RunProcess("xattr", "-d", "com.apple.quarantine", executable);
            RunProcess("codesign", "--force", "--sign", "-", executable);
        }
    }

    private static void RunProcess(string file, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo);
        process?.WaitForExit();
    }
}
