using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Storage;
using Fluence.Core.Models.Dotnet;
using Fluence.Infrastructure.Languages;

namespace Fluence.Infrastructure;

public sealed class DotnetSdkProvisioningService(
    IProcessHost processHost,
    IFluenceStorageService storage) : IDotnetSdkProvisioningService
{
    private const string RecommendedVersion = ".NET 10 SDK";
    private const string RecommendedChannel = "10.0";
    private const string DownloadUrl = "https://dotnet.microsoft.com/download";
    private const string BashInstallScriptUrl = "https://dot.net/v1/dotnet-install.sh";
    private const string PowerShellInstallScriptUrl = "https://dot.net/v1/dotnet-install.ps1";

    public async Task<DotnetSdkStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var managedDotnet = GetManagedDotnetPath();
        if (File.Exists(managedDotnet))
        {
            var managedStatus = await GetStatusForExecutableAsync(managedDotnet, cancellationToken).ConfigureAwait(false);
            if (managedStatus.IsDotnetAvailable && managedStatus.InstalledSdks.Count > 0)
                return managedStatus;
        }

        foreach (var candidate in GetWellKnownDotnetPaths())
        {
            if (!File.Exists(candidate))
                continue;
            var status = await GetStatusForExecutableAsync(candidate, cancellationToken).ConfigureAwait(false);
            if (status.IsDotnetAvailable && status.InstalledSdks.Count > 0)
                return status;
        }

        return await GetStatusForExecutableAsync("dotnet", cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<string> GetWellKnownDotnetPaths()
        => ProcessEnvironment.GetWellKnownDotnetPaths();

    public async Task<string> ResolveDotnetExecutableAsync(CancellationToken cancellationToken = default)
    {
        var status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(status.DotnetPath) ? "dotnet" : status.DotnetPath;
    }

    public async Task InstallIdeManagedSdkAsync(
        string channel,
        string displayName,
        Action<string> onOutput,
        CancellationToken cancellationToken = default)
    {
        var installDir = GetManagedInstallPath();
        Directory.CreateDirectory(installDir);

        channel = string.IsNullOrWhiteSpace(channel) ? RecommendedChannel : channel.Trim();
        displayName = string.IsNullOrWhiteSpace(displayName) ? $".NET {channel} SDK" : displayName.Trim();

        onOutput($"[Fluence] Installing {displayName} into {installDir}");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await InstallWithPowerShellAsync(channel, installDir, onOutput, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await InstallWithBashAsync(channel, installDir, onOutput, cancellationToken).ConfigureAwait(false);
        }

        onOutput("[Fluence] SDK installation finished. Refreshing detection...");
    }

    public Task OpenSystemWideInstallerAsync(Action<string> onOutput, CancellationToken cancellationToken = default)
    {
        onOutput("[Fluence] Opening official system-wide .NET SDK installer page.");
        onOutput("[Fluence] If the OS asks for permission, approve it to install for all users.");
        OpenUrl(DownloadUrl);
        return Task.CompletedTask;
    }

    private async Task<DotnetSdkStatus> GetStatusForExecutableAsync(
        string executable,
        CancellationToken cancellationToken)
    {
        var output = new List<string>();
        var errors = new List<string>();

        try
        {
            await processHost.RunAsync(
                executable,
                "--list-sdks",
                null,
                output.Add,
                errors.Add,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DotnetSdkStatus(false, null, GetManagedInstallPath(), RecommendedVersion, [], ex.Message);
        }

        return new DotnetSdkStatus(
            true,
            Path.IsPathRooted(executable)
                ? executable
                : await ResolvePathDotnetPathAsync(cancellationToken).ConfigureAwait(false),
            GetManagedInstallPath(),
            RecommendedVersion,
            ParseSdks(output),
            errors.Count > 0 ? string.Join(Environment.NewLine, errors) : null);
    }

    private async Task InstallWithBashAsync(
        string channel,
        string installDir,
        Action<string> onOutput,
        CancellationToken cancellationToken)
    {
        var scriptPath = Path.Combine(storage.GetUserPath("installers"), "dotnet-install.sh");
        await DownloadFileAsync(BashInstallScriptUrl, scriptPath, cancellationToken).ConfigureAwait(false);

        await processHost.RunAsync(
            "/bin/bash",
            [scriptPath, "--channel", channel, "--install-dir", installDir, "--skip-non-versioned-files"],
            null,
            onOutput,
            onOutput,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task InstallWithPowerShellAsync(
        string channel,
        string installDir,
        Action<string> onOutput,
        CancellationToken cancellationToken)
    {
        var scriptPath = Path.Combine(storage.GetUserPath("installers"), "dotnet-install.ps1");
        await DownloadFileAsync(PowerShellInstallScriptUrl, scriptPath, cancellationToken).ConfigureAwait(false);

        await processHost.RunAsync(
            "powershell",
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath, "-Channel", channel, "-InstallDir", installDir, "-SkipNonVersionedFiles"],
            null,
            onOutput,
            onOutput,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task DownloadFileAsync(
        string url,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using var httpClient = new HttpClient();
        await using var stream = await httpClient.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
        await using var file = File.Create(destinationPath);
        await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ResolvePathDotnetPathAsync(CancellationToken cancellationToken)
    {
        var output = new List<string>();

        try
        {
            var command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which";
            await processHost.RunAsync(command, ["dotnet"], null, output.Add, _ => { }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        return output.Count > 0 ? output[0] : null;
    }

    private string GetManagedInstallPath() => storage.GetUserPath("dotnet");

    private string GetManagedDotnetPath()
    {
        return Path.Combine(GetManagedInstallPath(), DotnetPlatformConstants.DotnetExecutableName);
    }

    private static IReadOnlyList<DotnetSdkInfo> ParseSdks(IEnumerable<string> lines)
    {
        var sdks = new List<DotnetSdkInfo>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var separator = trimmed.IndexOf(' ', StringComparison.Ordinal);
            if (separator <= 0)
                continue;

            var version = trimmed[..separator];
            var pathStart = trimmed.IndexOf('[', separator);
            var pathEnd = trimmed.LastIndexOf(']');
            var path = pathStart >= 0 && pathEnd > pathStart
                ? trimmed[(pathStart + 1)..pathEnd]
                : string.Empty;

            sdks.Add(new DotnetSdkInfo(version, path));
        }

        return sdks;
    }

    private static void OpenUrl(string url)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return;
        }

        var tool = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "open" : "xdg-open";
        Process.Start(new ProcessStartInfo(tool, url) { UseShellExecute = false });
    }
}
