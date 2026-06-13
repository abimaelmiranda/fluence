using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Infrastructure.Protocols.Dap;

public sealed class NetcoredbgToolService
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/Samsung/netcoredbg/releases/latest";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
    };

    static NetcoredbgToolService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("FluenceIDE");
    }

    public async Task<string> ResolveAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        var bundled = FindBundledTool(workspaceRoot);
        if (bundled is not null)
            return bundled;

        var appBundled = FindAppBundledTool();
        if (appBundled is not null)
            return appBundled;

        var pathTool = await FindOnPathAsync(cancellationToken).ConfigureAwait(false);
        if (pathTool is not null)
            return pathTool;

        var vsdbg = await FindVsdbgAsync(cancellationToken).ConfigureAwait(false);
        if (vsdbg is not null)
            return vsdbg;

        try
        {
            var downloaded = await DownloadLatestAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
            if (downloaded is not null)
                return downloaded;
        }
        catch
        {
        }

        throw new FileNotFoundException(
            "No .NET debugger found. Install vsdbg (`curl -sSL https://aka.ms/getvsdbgsh | bash /dev/stdin -v latest -l ~/.vsdbg`) " +
            "or netcoredbg and make sure it is on PATH.");
    }

    private static string? FindBundledTool(string workspaceRoot)
    {
        var toolsRoot = Path.Combine(workspaceRoot, ".fluence", "tools", "netcoredbg");
        if (!Directory.Exists(toolsRoot))
            return null;

        foreach (var file in Directory.EnumerateFiles(toolsRoot, ResolveExecutableName(), SearchOption.AllDirectories))
        {
            if (File.Exists(file))
                return file;
        }

        return null;
    }

    // Looks for netcoredbg shipped alongside the IDE itself under tools/netcoredbg/<rid>/
    private static string? FindAppBundledTool()
    {
        var appDir = AppContext.BaseDirectory;
        var rid = ResolveRuntimeId();
        var candidate = Path.Combine(appDir, "tools", "netcoredbg", rid, ResolveExecutableName());
        return File.Exists(candidate) ? candidate : null;
    }

    private static async Task<string?> FindOnPathAsync(CancellationToken cancellationToken)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var executable in new[] { ResolveExecutableName(), ResolveVsdbgName() })
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var candidate = Path.Combine(directory, executable);
                if (!File.Exists(candidate))
                    continue;

                if (await ValidateAsync(candidate, cancellationToken).ConfigureAwait(false))
                    return candidate;
            }
        }

        return null;
    }

    private static async Task<string?> FindVsdbgAsync(CancellationToken cancellationToken)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(home, ".vsdbg", ResolveVsdbgName()),
            Path.Combine(home, ".vs-debugger", ResolveVsdbgName()),
            "/usr/local/share/vsdbg/vsdbg",
        };

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate))
                continue;

            if (await ValidateAsync(candidate, cancellationToken).ConfigureAwait(false))
                return candidate;
        }

        return null;
    }

    private static async Task<string?> DownloadLatestAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(LatestReleaseApiUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var tag = document.RootElement.GetProperty("tag_name").GetString() ?? "latest";
        var asset = FindAsset(document.RootElement.GetProperty("assets"));
        if (asset is null)
            return null;

        var installRoot = Path.Combine(workspaceRoot, ".fluence", "tools", "netcoredbg", tag, ResolveRuntimeId());
        if (Directory.Exists(installRoot))
            Directory.Delete(installRoot, recursive: true);
        Directory.CreateDirectory(installRoot);

        var archivePath = Path.Combine(Path.GetTempPath(), asset.Value.Name);
        await using (var archive = File.Create(archivePath))
        using (var assetResponse = await Http.GetAsync(asset.Value.DownloadUrl, cancellationToken).ConfigureAwait(false))
        {
            assetResponse.EnsureSuccessStatusCode();
            await assetResponse.Content.CopyToAsync(archive, cancellationToken).ConfigureAwait(false);
        }

        ExtractArchive(archivePath, installRoot);
        var executable = FindExecutable(installRoot);
        if (executable is null)
            return null;

        MakeExecutable(executable);
        await File.WriteAllTextAsync(
            Path.Combine(installRoot, "manifest.json"),
            JsonSerializer.Serialize(new { tag, asset = asset.Value.Name, url = asset.Value.DownloadUrl }, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken).ConfigureAwait(false);

        return await ValidateAsync(executable, cancellationToken).ConfigureAwait(false)
            ? executable
            : null;
    }

    private static (string Name, string DownloadUrl)? FindAsset(JsonElement assets)
    {
        var rid = ResolveRuntimeId();
        (string Name, string DownloadUrl)? fallback = null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                continue;

            var lower = name.ToLowerInvariant();

            // Exact RID match (e.g. osx-arm64, osx-x64, linux-x64)
            if (lower.Contains(rid))
                return (name, url);

            // OS-only fallback — only accept when architecture also matches to avoid
            // silently downloading an incompatible binary (e.g. osx-amd64 on an arm64 host).
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                (lower.Contains("osx") || lower.Contains("macos")))
            {
                var isAmd64Asset = lower.Contains("amd64") || lower.Contains("x64") || lower.Contains("x86_64");
                var isArm64Host = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
                if (!isAmd64Asset || !isArm64Host)
                    fallback ??= (name, url);
            }
        }

        return fallback;
    }

    private static void ExtractArchive(string archivePath, string installRoot)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, installRoot, overwriteFiles: true);
            return;
        }

        if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "tar",
                ArgumentList = { "-xzf", archivePath, "-C", installRoot },
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            process?.WaitForExit();
        }
    }

    private static string? FindExecutable(string installRoot)
    {
        foreach (var file in Directory.EnumerateFiles(installRoot, ResolveExecutableName(), SearchOption.AllDirectories))
            return file;

        return null;
    }

    private static async Task<bool> ValidateAsync(string executable, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                ArgumentList = { "--help" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
                return false;

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void MakeExecutable(string executable)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        RunProcess("chmod", ["+x", executable]);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Remove the quarantine attribute macOS adds to internet downloads.
            // Without this, macOS may block netcoredbg from spawning a child process for debugging.
            RunProcess("xattr", ["-d", "com.apple.quarantine", executable]);
            // Re-sign ad-hoc to clear any invalid third-party signature from the archive.
            RunProcess("codesign", ["--force", "--sign", "-", executable]);
        }
    }

    private static void RunProcess(string file, string[] args)
    {
        var psi = new ProcessStartInfo { FileName = file, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        p?.WaitForExit();
    }

    private static string ResolveExecutableName() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "netcoredbg.exe" : "netcoredbg";

    private static string ResolveVsdbgName() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "vsdbg.exe" : "vsdbg";

    private static string ResolveRuntimeId()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? "osx"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "win"
                : "linux";
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        return $"{os}-{arch}";
    }
}
