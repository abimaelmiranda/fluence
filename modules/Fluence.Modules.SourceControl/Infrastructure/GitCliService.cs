using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Modules.SourceControl.Abstractions;
using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.Infrastructure;

public sealed partial class GitCliService(IProcessSpawner spawner) : IGitService
{
    private static readonly string GitExecutable = GitExecutableResolver.Current.Resolve();

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

    private async Task<string> RunGitAsync(string workingDirectory, params string[] args)
    {
        await using var tracked = await CreateProcessAsync(workingDirectory, args).ConfigureAwait(false);
        var process = tracked.Process;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output.TrimEnd(); // TrimEnd only: leading spaces are meaningful in porcelain format.
    }

    private async Task<string> RunGitWithStderrAsync(string workingDirectory, params string[] args)
    {
        var (output, _) = await RunGitWithExitCodeAsync(workingDirectory, args);
        return output;
    }

    private async Task<(string Output, int ExitCode)> RunGitWithExitCodeAsync(string workingDirectory, params string[] args)
    {
        await using var tracked = await CreateProcessAsync(workingDirectory, args).ConfigureAwait(false);
        var process = tracked.Process;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var combined = (await stdout) + (await stderr);
        return (combined.Trim(), process.ExitCode);
    }

    private Task<ITrackedProcess> CreateProcessAsync(string workingDirectory, IReadOnlyList<string> args)
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

        return spawner.StartAsync(startInfo, "Git");
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
