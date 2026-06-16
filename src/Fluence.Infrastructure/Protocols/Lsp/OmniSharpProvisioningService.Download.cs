using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Fluence.Infrastructure.Protocols.Lsp;

public sealed partial class OmniSharpProvisioningService
{
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

        var installDir = InstallDir;
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
}
