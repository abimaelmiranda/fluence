using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.LanguageServer;

namespace Fluence.Infrastructure.Protocols.Lsp;

public sealed class OmniSharpProvisioningService : ILspProvisioningService
{
    private const string OmniSharpApiUrl = "https://api.github.com/repos/OmniSharp/omnisharp-roslyn/releases/latest";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    static OmniSharpProvisioningService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("FluenceIDE");
    }

    public bool IsProvisioned() => File.Exists(GetExecutablePath());

    public string GetExecutablePath()
    {
        var dir = ResolveGlobalInstallDir();
        return Path.Combine(dir, ResolveExecutableName());
    }

    public IReadOnlyDictionary<string, string> GetLaunchEnvironment()
    {
        var dotnetRoot = ResolveDotnetDir();
        return new Dictionary<string, string>
        {
            ["DOTNET_ROOT"] = dotnetRoot,
        };
    }

    public async Task ProvisionAsync(Action<string> onOutput, CancellationToken cancellationToken = default)
    {
        await EnsureInstallDirectoryAsync(onOutput, cancellationToken).ConfigureAwait(false);
        await DownloadBinaryAsync(onOutput, cancellationToken).ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Permission elevation — create the global install directory
    // -----------------------------------------------------------------------

    private static async Task EnsureInstallDirectoryAsync(Action<string> onOutput, CancellationToken cancellationToken)
    {
        var installDir = ResolveGlobalInstallDir();

        if (Directory.Exists(installDir))
            return;

        try
        {
            Directory.CreateDirectory(installDir);
            return;
        }
        catch (UnauthorizedAccessException) { }

        onOutput("[Fluence] Administrator privileges required to create the install directory.");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            await ElevateOnMacOsAsync(installDir, onOutput, cancellationToken).ConfigureAwait(false);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await ElevateOnLinuxAsync(installDir, onOutput, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new UnauthorizedAccessException(
                $"Cannot create '{installDir}'. Run Fluence as Administrator for the first-time language server setup.");
        }

        if (!Directory.Exists(installDir))
            throw new InvalidOperationException(
                $"Failed to create install directory '{installDir}'. Check your permissions and try again.");
    }

    private static async Task ElevateOnMacOsAsync(string installDir, Action<string> onOutput, CancellationToken cancellationToken)
    {
        var currentUser = Environment.GetEnvironmentVariable("USER") ?? Environment.UserName;
        var shellCmd = $"mkdir -p '{installDir}' && chown -R {currentUser} '{installDir}'";
        var appleScript = $"do shell script \"{shellCmd}\" with administrator privileges";

        onOutput("[Fluence] A system dialog will ask for your password...");

        var psi = new ProcessStartInfo
        {
            FileName = "osascript",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(appleScript);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to launch osascript.");

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var reason = string.IsNullOrWhiteSpace(stderr) ? "user cancelled or password incorrect" : stderr.Trim();
            throw new UnauthorizedAccessException($"Privilege elevation failed: {reason}");
        }

        onOutput("[Fluence] Directory created successfully.");
    }

    private static async Task ElevateOnLinuxAsync(string installDir, Action<string> onOutput, CancellationToken cancellationToken)
    {
        var currentUser = Environment.GetEnvironmentVariable("USER") ?? Environment.UserName;
        onOutput("[Fluence] A system dialog will ask for your password...");

        var psi = new ProcessStartInfo
        {
            FileName = "pkexec",
            Arguments = $"bash -c \"mkdir -p '{installDir}' && chown -R {currentUser} '{installDir}'\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to launch pkexec.");

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var reason = string.IsNullOrWhiteSpace(stderr) ? "user cancelled or pkexec not available" : stderr.Trim();
            throw new UnauthorizedAccessException($"Privilege elevation failed: {reason}");
        }

        onOutput("[Fluence] Directory created successfully.");
    }

    // -----------------------------------------------------------------------
    // Download
    // -----------------------------------------------------------------------

    private async Task DownloadBinaryAsync(Action<string> onOutput, CancellationToken cancellationToken)
    {
        onOutput("[Fluence] Fetching latest OmniSharp release from GitHub...");

        using var response = await Http.GetAsync(OmniSharpApiUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var tag = document.RootElement.GetProperty("tag_name").GetString() ?? "latest";
        onOutput($"[Fluence] Found release: {tag}");

        var asset = FindOmniSharpAsset(document.RootElement.GetProperty("assets"));
        if (asset is null)
            throw new InvalidOperationException("No compatible OmniSharp asset found for this platform.");

        onOutput($"[Fluence] Downloading {asset.Value.Name}...");

        var installDir = ResolveGlobalInstallDir();
        if (Directory.Exists(installDir))
            Directory.Delete(installDir, recursive: true);
        Directory.CreateDirectory(installDir);

        var archivePath = Path.Combine(Path.GetTempPath(), asset.Value.Name);
        await using (var file = File.Create(archivePath))
        using (var assetResponse = await Http.GetAsync(asset.Value.DownloadUrl, cancellationToken).ConfigureAwait(false))
        {
            assetResponse.EnsureSuccessStatusCode();
            await assetResponse.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        onOutput($"[Fluence] Extracting {asset.Value.Name}...");
        ExtractArchive(archivePath, installDir);

        var executable = Directory
            .EnumerateFiles(installDir, ResolveExecutableName(), SearchOption.AllDirectories)
            .FirstOrDefault()
            ?? throw new FileNotFoundException("OmniSharp executable not found after extraction.");

        onOutput("[Fluence] Applying permissions...");
        MakeExecutable(executable);

        onOutput($"[Fluence] OmniSharp installed at: {executable}");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (string Name, string DownloadUrl)? FindOmniSharpAsset(JsonElement assets)
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

            // Skip non-archive assets (e.g. checksums, source zips)
            if (!lower.EndsWith(".tar.gz", StringComparison.Ordinal) && !lower.EndsWith(".zip", StringComparison.Ordinal))
                continue;

            // OmniSharp releases use names like: omnisharp-osx-arm64-net6.0.tar.gz
            // Prefer the exact RID match
            if (lower.Contains(rid, StringComparison.Ordinal))
                return (name, url);

            // Fallback: same OS family, any arch
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && lower.Contains("osx", StringComparison.Ordinal))
                fallback ??= (name, url);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && lower.Contains("win", StringComparison.Ordinal))
                fallback ??= (name, url);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && lower.Contains("linux", StringComparison.Ordinal))
                fallback ??= (name, url);
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
            RunProcess("tar", $"-xzf \"{archivePath}\" -C \"{installRoot}\"");
        }
    }

    private static void MakeExecutable(string executable)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        RunProcess("chmod", $"+x \"{executable}\"");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            RunProcess("xattr", $"-d com.apple.quarantine \"{executable}\"");
            RunProcess("codesign", $"--force --sign - \"{executable}\"");
        }
    }

    private static void RunProcess(string file, string arguments)
    {
        using var p = Process.Start(new ProcessStartInfo
        {
            FileName = file,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        p?.WaitForExit();
    }

    internal static string ResolveGlobalInstallDir()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".fluence", "LanguageServer");
    }

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
