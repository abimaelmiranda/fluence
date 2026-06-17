using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Fluence.Modules.SourceControl.Abstractions;
using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.Infrastructure;

public sealed partial class GitCliService : IGitService
{
    /// <summary>
    /// Resolves <c>git</c> to an absolute path once at class-load time.
    /// On macOS <c>/usr/bin/git</c> always exists on the minimal PATH,
    /// but scanning also picks up Homebrew installs when present.
    /// Falls back to the bare name <c>"git"</c> so the OS can still
    /// locate it through any mechanism not covered by our PATH scan.
    /// </summary>
    private static readonly string GitExecutable = ResolveGitExecutable();

    private static string ResolveGitExecutable()
    {
        try
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                var candidate = Path.Combine(dir.Trim(), "git");
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        catch
        {
            // Best-effort; fall back to bare name.
        }

        return "git";
    }
    public async Task<string?> GetRepositoryRootAsync(string workingDirectory)
    {
        try
        {
            var result = await RunGitAsync(workingDirectory, "rev-parse", "--show-toplevel");
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch (System.OperationCanceledException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            Debug.WriteLine($"Git repository root lookup failed: {ex}");
            return null;
        }
    }

    private static async Task<string> RunGitAsync(string workingDirectory, params string[] args)
    {
        using var process = CreateProcess(workingDirectory, args);
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output.TrimEnd(); // TrimEnd only: leading spaces are meaningful in porcelain format.
    }

    private static async Task<string> RunGitWithStderrAsync(string workingDirectory, params string[] args)
    {
        var (output, _) = await RunGitWithExitCodeAsync(workingDirectory, args);
        return output;
    }

    private static async Task<(string Output, int ExitCode)> RunGitWithExitCodeAsync(string workingDirectory, params string[] args)
    {
        using var process = CreateProcess(workingDirectory, args);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var combined = (await stdout) + (await stderr);
        return (combined.Trim(), process.ExitCode);
    }

    private static Process CreateProcess(string workingDirectory, IReadOnlyList<string> args)
    {
        var startInfo = new ProcessStartInfo(GitExecutable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        return new Process { StartInfo = startInfo };
    }

    private static GitChangeStatus ParseStatus(char c) => c switch
    {
        'M' => GitChangeStatus.Modified,
        'A' => GitChangeStatus.Added,
        'D' => GitChangeStatus.Deleted,
        'R' => GitChangeStatus.Renamed,
        'U' => GitChangeStatus.Conflicted,
        _ => GitChangeStatus.Modified,
    };
}
