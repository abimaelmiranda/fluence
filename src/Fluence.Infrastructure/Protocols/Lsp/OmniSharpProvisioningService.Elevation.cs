using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Infrastructure.Protocols.Lsp;

public sealed partial class OmniSharpProvisioningService
{
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
}
