using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Fluence.Infrastructure;

/// <summary>
/// Single source of truth for the augmented <c>PATH</c> and <c>DOTNET_ROOT</c>
/// that child processes (dotnet, netcoredbg, git, cmake, ...) inherit.
/// </summary>
/// <remarks>
/// When the IDE is launched as a macOS <c>.app</c> from Finder/Dock, the process
/// inherits a minimal <c>PATH</c> (typically just <c>/usr/bin:/bin:/usr/sbin:/sbin</c>).
/// Tools installed by Homebrew (<c>/opt/homebrew/bin</c>) or the .NET installer
/// (<c>/usr/local/share/dotnet</c>) are not on that path, so the debugger fails to
/// launch the debuggee with <c>0x80070002</c> (E_FILE_NOT_FOUND). Augmenting the
/// environment at startup restores parity with running from a shell (VSCode).
/// </remarks>
public static class ProcessEnvironment
{
    /// <summary>
    /// Directories to prepend/append to <c>PATH</c> when present on disk.
    /// Order matters: the first matching directory wins when resolving a tool.
    /// </summary>
    public static IEnumerable<string> GetWellKnownPathDirectories()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Apple Silicon Homebrew first (most common on arm64), then Intel.
            yield return "/opt/homebrew/bin";
            yield return "/usr/local/bin";
            yield return "/usr/local/share/dotnet";
            yield return "/opt/homebrew/share/dotnet";
            yield return Path.Combine(home, ".dotnet");
            yield return Path.Combine(home, ".dotnet", "tools");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            yield return "/usr/local/bin";
            yield return "/usr/share/dotnet";
            yield return "/usr/lib/dotnet";
            yield return Path.Combine(home, ".dotnet");
            yield return Path.Combine(home, ".dotnet", "tools");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return Path.Combine(home, ".dotnet", "tools");
        }
    }

    /// <summary>
    /// Candidate <c>dotnet</c> executable paths to probe, in priority order.
    /// Reused by SDK provisioning and the DAP launch flow so both agree on the
    /// same runtime location.
    /// </summary>
    public static IEnumerable<string> GetWellKnownDotnetPaths()
    {
        var exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet.exe" : "dotnet";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // IDE-managed install first (provisioned under ~/.fluence/dotnet).
        yield return Path.Combine(home, ".fluence", "dotnet", exe);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            yield return "/usr/local/share/dotnet/dotnet";
            yield return "/opt/homebrew/share/dotnet/dotnet";
            yield return "/opt/homebrew/bin/dotnet";
            yield return "/opt/homebrew/opt/dotnet/libexec/dotnet";
            yield return Path.Combine(home, ".dotnet", exe);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            yield return "/usr/share/dotnet/dotnet";
            yield return "/usr/lib/dotnet/dotnet";
            yield return "/usr/bin/dotnet";
            yield return Path.Combine(home, ".dotnet", exe);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet",
                exe);
            yield return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "dotnet",
                exe);
            yield return Path.Combine(home, ".dotnet", exe);
        }
    }

    /// <summary>
    /// Returns the current <c>PATH</c> augmented with well-known directories that
    /// actually exist on disk, preserving the original entries (no duplicates).
    /// </summary>
    public static string GetAugmentedPath()
    {
        var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var entries = new List<string>(current.Split(new[] { Path.PathSeparator }, StringSplitOptions.None));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(entry))
                seen.Add(entry.Trim());
        }

        foreach (var dir in GetWellKnownPathDirectories())
        {
            if (!Directory.Exists(dir))
                continue;
            var normalized = dir.Trim();
            if (seen.Add(normalized))
                entries.Add(normalized);
        }

        return string.Join(Path.PathSeparator, entries);
    }

    /// <summary>
    /// Resolves the directory containing the <c>dotnet</c> executable, probing the
    /// well-known locations first. Returns <c>null</c> when nothing is found.
    /// </summary>
    public static string? ResolveDotnetRoot()
    {
        foreach (var candidate in GetWellKnownDotnetPaths())
        {
            if (File.Exists(candidate))
                return Path.GetDirectoryName(candidate);
        }

        return null;
    }

    /// <summary>
    /// Augments <c>PATH</c> (and sets <c>DOTNET_ROOT</c> when dotnet is found) for the
    /// current process so that every spawned child inherits a usable environment.
    /// Safe to call multiple times; never throws.
    /// </summary>
    public static void ApplyToCurrentProcess()
    {
        try
        {
            Environment.SetEnvironmentVariable("PATH", GetAugmentedPath());

            var dotnetRoot = ResolveDotnetRoot();
            if (dotnetRoot is not null && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOTNET_ROOT")))
                Environment.SetEnvironmentVariable("DOTNET_ROOT", dotnetRoot);
        }
        catch
        {
            // Environment augmentation is best-effort; never let it crash startup.
        }
    }
}
