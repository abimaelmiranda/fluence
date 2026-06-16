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
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Services.Debugging;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Storage;
using Fluence.Core.Models.Infrastructure;

namespace Fluence.Infrastructure.Protocols.Dap;

public sealed class DebuggerProvisioningService(IProcessHost processHost, IFluenceStorageService storage) : IDebuggerProvisioningService
{
    private const string NetcoredbgApiUrl = "https://api.github.com/repos/Samsung/netcoredbg/releases/latest";
    private const string CmakeApiUrl = "https://api.github.com/repos/Kitware/CMake/releases/latest";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    static DebuggerProvisioningService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("FluenceIDE");
    }

    public bool IsProvisioned() => File.Exists(GetExecutablePath());

    public string GetExecutablePath()
    {
        var dir = ResolveGlobalInstallDir();
        return Path.Combine(dir, ResolveExecutableName());
    }

    public async Task ProvisionAsync(Action<string> onOutput, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ResolveGlobalInstallDir());

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS has no stable official netcoredbg binary; always build from source
            await BuildFromSourceAsync(onOutput, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await DownloadBinaryAsync(onOutput, cancellationToken).ConfigureAwait(false);
        }
    }

    // -----------------------------------------------------------------------
    // Download path (Windows / Linux)
    // -----------------------------------------------------------------------

    private async Task DownloadBinaryAsync(Action<string> onOutput, CancellationToken cancellationToken)
    {
        onOutput("[Fluence] Fetching latest netcoredbg release from GitHub...");

        using var response = await Http.GetAsync(NetcoredbgApiUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var tag = document.RootElement.GetProperty("tag_name").GetString() ?? "latest";
        onOutput($"[Fluence] Found release: {tag}");

        var asset = FindNetcoredbgAsset(document.RootElement.GetProperty("assets"));
        if (asset is null)
            throw new InvalidOperationException("No compatible netcoredbg asset found for this platform.");

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
            ?? throw new FileNotFoundException("netcoredbg executable not found after extraction.");

        onOutput("[Fluence] Applying permissions...");
        MakeExecutable(executable);

        onOutput($"[Fluence] netcoredbg installed at: {executable}");
    }

    // -----------------------------------------------------------------------
    // Build from source path (macOS)
    // -----------------------------------------------------------------------

    private async Task BuildFromSourceAsync(Action<string> onOutput, CancellationToken cancellationToken)
    {
        var installDir = ResolveGlobalInstallDir();
        var tempDir = Path.Combine(installDir, "temp");

        Directory.CreateDirectory(installDir);
        Directory.CreateDirectory(tempDir);

        try
        {
            var cmake = await ResolveCmakeAsync(tempDir, onOutput, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "cmake not found. Install it with 'brew install cmake', 'port install cmake', or download from https://cmake.org/download/");

            onOutput($"[Fluence] Using cmake: {cmake}");

            var dotnetDir = ResolveDotnetDir();
            onOutput($"[Fluence] Using .NET SDK at: {dotnetDir}");

            var netcoredbgSrcDir = Path.Combine(tempDir, "netcoredbg");
            var buildDir = Path.Combine(tempDir, "build");

            onOutput("[Fluence] Cloning Samsung/netcoredbg (shallow)...");
            await RunAsync("git", $"clone --depth=1 https://github.com/Samsung/netcoredbg \"{netcoredbgSrcDir}\"",
                tempDir, onOutput, cancellationToken).ConfigureAwait(false);

            Directory.CreateDirectory(buildDir);

            var cores = GetLogicalCpuCount();
            onOutput("[Fluence] Configuring build with cmake...");
            // netcoredbg's cmake locates CoreCLR headers from DOTNET_DIR — no separate runtime clone needed
            // CMAKE_INSTALL_PREFIX directs `cmake --install` to place all artifacts (dylib + managed DLLs) in installDir
            await RunAsync(cmake,
                $"-S \"{netcoredbgSrcDir}\" -B \"{buildDir}\" -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX=\"{installDir}\" -DDOTNET_DIR=\"{dotnetDir}\"",
                tempDir, onOutput, cancellationToken).ConfigureAwait(false);

            onOutput($"[Fluence] Building netcoredbg (-j{cores})...");
            await RunAsync(cmake, $"--build \"{buildDir}\" -j{cores}",
                tempDir, onOutput, cancellationToken).ConfigureAwait(false);

            onOutput("[Fluence] Installing netcoredbg and companion libraries...");
            await RunAsync(cmake, $"--install \"{buildDir}\"",
                tempDir, onOutput, cancellationToken).ConfigureAwait(false);

            var targetExecutable = GetExecutablePath();
            if (!File.Exists(targetExecutable))
                throw new FileNotFoundException("netcoredbg not found after install.", targetExecutable);

            MakeExecutable(targetExecutable);
            onOutput($"[Fluence] netcoredbg installed at: {targetExecutable}");
        }
        finally
        {
            TryDeleteDirectory(tempDir, onOutput);
        }
    }

    // -----------------------------------------------------------------------
    // cmake resolution (macOS fallback chain)
    // -----------------------------------------------------------------------

    private async Task<string?> ResolveCmakeAsync(string tempDir, Action<string> onOutput, CancellationToken cancellationToken)
    {
        // 1. Already on PATH
        var onPath = FindOnPath("cmake");
        if (onPath is not null)
            return onPath;

        // 2. CMake.app bundle
        const string appBundle = "/Applications/CMake.app/Contents/bin/cmake";
        if (File.Exists(appBundle))
            return appBundle;

        // 3. Homebrew
        var brew = FindBrewPath();
        if (brew is not null)
        {
            onOutput("[Fluence] cmake not found. Installing via Homebrew...");
            try
            {
                await RunAsync(brew, "install cmake", null, onOutput, cancellationToken).ConfigureAwait(false);
                var afterBrew = FindOnPath("cmake")
                    ?? FindAtKnownBrewPaths();
                if (afterBrew is not null)
                    return afterBrew;
            }
            catch { }
        }

        // 4. MacPorts
        var port = FindOnPath("port");
        if (port is not null)
        {
            onOutput("[Fluence] cmake not found. Installing via MacPorts...");
            try
            {
                await RunAsync(port, "install cmake", null, onOutput, cancellationToken).ConfigureAwait(false);
                var afterPort = FindOnPath("cmake");
                if (afterPort is not null)
                    return afterPort;
            }
            catch { }
        }

        // 5. Download cmake binary from GitHub
        onOutput("[Fluence] cmake not found via package managers. Downloading cmake binary...");
        try
        {
            var cmakePath = await DownloadCmakeAsync(tempDir, onOutput, cancellationToken).ConfigureAwait(false);
            if (cmakePath is not null)
                return cmakePath;
        }
        catch (Exception ex)
        {
            onOutput($"[Fluence] Failed to download cmake: {ex.Message}");
        }

        return null;
    }

    private static string? FindBrewPath()
    {
        // Apple Silicon brew is at /opt/homebrew, Intel brew at /usr/local
        string[] candidates = ["/opt/homebrew/bin/brew", "/usr/local/bin/brew"];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindAtKnownBrewPaths()
    {
        string[] candidates = ["/opt/homebrew/bin/cmake", "/usr/local/bin/cmake"];
        return candidates.FirstOrDefault(File.Exists);
    }

    private async Task<string?> DownloadCmakeAsync(string tempDir, Action<string> onOutput, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(CmakeApiUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        (string Name, string DownloadUrl)? asset = null;
        foreach (var a in document.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString() ?? "";
            var url = a.GetProperty("browser_download_url").GetString() ?? "";
            if (name.Contains("macos-universal", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                asset = (name, url);
                break;
            }
        }

        if (asset is null)
            return null;

        onOutput($"[Fluence] Downloading {asset.Value.Name}...");
        var archivePath = Path.Combine(Path.GetTempPath(), asset.Value.Name);
        await using (var file = File.Create(archivePath))
        using (var assetResponse = await Http.GetAsync(asset.Value.DownloadUrl, cancellationToken).ConfigureAwait(false))
        {
            assetResponse.EnsureSuccessStatusCode();
            await assetResponse.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        var extractDir = Path.Combine(tempDir, "cmake");
        Directory.CreateDirectory(extractDir);
        ExtractArchive(archivePath, extractDir);

        // Executable is inside CMake.app bundle in the extracted folder
        var executable = Directory
            .EnumerateFiles(extractDir, "cmake", SearchOption.AllDirectories)
            .FirstOrDefault(f => f.Contains("bin/cmake", StringComparison.OrdinalIgnoreCase) &&
                                 !f.EndsWith(".cmake", StringComparison.OrdinalIgnoreCase));

        if (executable is null)
            return null;

        RunProcess("chmod", $"+x \"{executable}\"");
        return executable;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private async Task RunAsync(string executable, string arguments, string? workingDirectory,
        Action<string> onOutput, CancellationToken cancellationToken)
    {
        await processHost.RunAsync(executable, arguments, workingDirectory, onOutput, onOutput, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string? FindOnPath(string executable)
    {
        var paths = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in paths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(dir, executable);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static (string Name, string DownloadUrl)? FindNetcoredbgAsset(JsonElement assets)
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
            if (lower.Contains(rid))
                return (name, url);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && lower.Contains("win"))
                fallback ??= (name, url);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && lower.Contains("linux"))
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

    private static string ResolveDotnetDir()
    {
        var dotnet = FindOnPath("dotnet");
        if (dotnet is not null)
            return Path.GetDirectoryName(dotnet)!;

        // Common macOS location
        const string fallback = "/usr/local/share/dotnet";
        return Directory.Exists(fallback) ? fallback : "/usr/local/share/dotnet";
    }

    private static int GetLogicalCpuCount()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "sysctl",
                Arguments = "-n hw.logicalcpu",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is not null)
            {
                var output = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit();
                if (int.TryParse(output, out var count) && count > 0)
                    return count;
            }
        }
        catch { }

        return Environment.ProcessorCount;
    }

    private static void TryDeleteDirectory(string path, Action<string> onOutput)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                onOutput("[Fluence] Cleaned up temporary build files.");
            }
        }
        catch (Exception ex)
        {
            onOutput($"[Fluence] Warning: failed to remove temp directory: {ex.Message}");
        }
    }

    internal string ResolveGlobalInstallDir() => storage.GetUserPath("debuggers/csharp");

    private static string ResolveExecutableName() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "netcoredbg.exe" : "netcoredbg";

    private static string ResolveRuntimeId()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
            : "linux";
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        return $"{os}-{arch}";
    }
}
