using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Storage;

namespace Fluence.Infrastructure.Protocols.Dap;

public sealed class CppDebuggerProvisioningService(
    IProcessHost processHost,
    IFluenceStorageService storage) : IDebuggerProvisioningService
{
    private const string FormulaApiUrl = "https://formulae.brew.sh/api/formula/llvm.json";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    static CppDebuggerProvisioningService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("FluenceIDE");
    }

    public bool IsProvisioned() =>
        TryFindExecutable() is not null;

    public string GetExecutablePath() =>
        TryFindExecutable() ?? "lldb-dap";

    public async Task ProvisionAsync(Action<string> onOutput, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = TryFindExecutable();
        if (existing is not null)
        {
            onOutput($"[Fluence] lldb-dap found at: {existing}");
            return;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            throw new InvalidOperationException("lldb-dap provisioning is currently supported on macOS only.");

        var brew = FindBrewPath();
        if (brew is not null)
        {
            onOutput("[Fluence] lldb-dap was not found on PATH.");
            onOutput("[Fluence] Trying Homebrew llvm...");
            await RunAsync(brew, "install llvm", null, onOutput, cancellationToken).ConfigureAwait(false);

            existing = TryFindExecutable();
            if (existing is not null)
            {
                onOutput($"[Fluence] lldb-dap is available at: {existing}");
                return;
            }
        }

        onOutput("[Fluence] Homebrew did not provide lldb-dap. Trying the official LLVM binary...");
        var installed = await InstallOfficialBinaryAsync(onOutput, cancellationToken).ConfigureAwait(false);
        if (installed is not null)
        {
            onOutput($"[Fluence] lldb-dap installed at: {installed}");
            return;
        }

        throw new InvalidOperationException(
            "Unable to provision lldb-dap automatically. Install Homebrew llvm or Xcode Command Line Tools, then retry.");
    }

    private async Task<string?> InstallOfficialBinaryAsync(Action<string> onOutput, CancellationToken cancellationToken)
    {
        var bottleUrl = await ResolveBottleUrlAsync(cancellationToken).ConfigureAwait(false);
        if (bottleUrl is null)
            return null;

        var installRoot = storage.GetUserPath("debuggers/lldb-dap");
        var tempRoot = Path.Combine(Path.GetTempPath(), "fluence-lldb-dap");
        var archivePath = Path.Combine(Path.GetTempPath(), $"llvm-{Guid.NewGuid():N}.tar.gz");

        TryDeleteDirectory(installRoot);
        TryDeleteDirectory(tempRoot);
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(tempRoot);

        try
        {
            onOutput($"[Fluence] Downloading official LLVM bottle: {bottleUrl}");
            await using (var file = File.Create(archivePath))
            using (var response = await Http.GetAsync(bottleUrl, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            onOutput("[Fluence] Extracting LLVM bottle...");
            await RunAsync("tar", $"-xzf \"{archivePath}\" -C \"{installRoot}\"", null, onOutput, cancellationToken).ConfigureAwait(false);

            var executable = TryFindExecutableInRoot(installRoot);
            if (executable is null)
                return null;

            MakeExecutable(executable);
            return executable;
        }
        finally
        {
            TryDeleteFile(archivePath);
            TryDeleteDirectory(tempRoot);
        }
    }

    private static async Task<string?> ResolveBottleUrlAsync(CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(FormulaApiUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty("bottle", out var bottle) ||
            !bottle.TryGetProperty("stable", out var stable) ||
            !stable.TryGetProperty("files", out var files))
        {
            return null;
        }

        var candidates = GetBottleCandidates();
        foreach (var key in candidates)
        {
            if (!files.TryGetProperty(key, out var file))
                continue;

            var url = file.GetProperty("url").GetString();
            if (!string.IsNullOrWhiteSpace(url))
                return url;
        }

        return null;
    }

    private static IEnumerable<string> GetBottleCandidates()
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            return ["arm64_tahoe", "arm64_sequoia", "arm64_sonoma", "sonoma"];

        return ["tahoe", "sequoia", "sonoma", "ventura", "monterey", "big_sur"];
    }

    private string? TryFindExecutable()
    {
        var onPath = PlatformTooling.Current.FindOnPath("lldb-dap");
        if (onPath is not null)
            return onPath;

        var xcrun = FindViaXcrun();
        if (xcrun is not null)
            return xcrun;

        foreach (var candidate in GetKnownExecutablePaths())
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return TryFindExecutableInRoot(storage.GetUserPath("debuggers/lldb-dap"));
    }

    private static string? FindViaXcrun()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "xcrun",
                Arguments = "--find lldb-dap",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            return process.ExitCode == 0 && File.Exists(output) ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> GetKnownExecutablePaths()
    {
        var paths = new List<string>
        {
            "/Library/Developer/CommandLineTools/usr/bin/lldb-dap",
            "/Applications/Xcode.app/Contents/Developer/Toolchains/XcodeDefault.xctoolchain/usr/bin/lldb-dap",
            "/opt/homebrew/opt/llvm/bin/lldb-dap",
            "/usr/local/opt/llvm/bin/lldb-dap",
        };

        return paths;
    }

    private static string? TryFindExecutableInRoot(string root)
    {
        if (!Directory.Exists(root))
            return null;

        return Directory.EnumerateFiles(root, "lldb-dap", SearchOption.AllDirectories)
            .FirstOrDefault(File.Exists);
    }

    private static string? FindBrewPath()
    {
        foreach (var candidate in new[]
                 {
                     "/opt/homebrew/bin/brew",
                     "/usr/local/bin/brew",
                 })
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private async Task RunAsync(string executable, string arguments, string? workingDirectory, Action<string> onOutput, CancellationToken cancellationToken)
    {
        var result = await processHost.RunWithResultAsync(
            executable,
            arguments,
            workingDirectory,
            line => onOutput(line),
            line => onOutput(line),
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
            throw new InvalidOperationException($"{Path.GetFileName(executable)} exited with code {result.ExitCode}.");
    }

    private static void MakeExecutable(string executable)
    {
        PlatformTooling.Current.MakeExecutable(executable);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
