using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Infrastructure;

namespace Fluence.Infrastructure;

public sealed class ProcessHost(IProcessSpawner spawner) : IProcessHost
{
    public async Task RunAsync(
        string executable,
        string arguments,
        string? workingDirectory,
        Action<string> onOutput,
        Action<string> onError,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        _ = await RunWithResultAsync(
            executable,
            arguments,
            workingDirectory,
            onOutput,
            onError,
            cancellationToken,
            environment).ConfigureAwait(false);
    }

    public async Task RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        Action<string> onOutput,
        Action<string> onError,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        _ = await RunWithResultAsync(
            executable,
            arguments,
            workingDirectory,
            onOutput,
            onError,
            cancellationToken,
            environment).ConfigureAwait(false);
    }

    public async Task<ProcessResult> RunWithResultAsync(
        string executable,
        string arguments,
        string? workingDirectory,
        Action<string> onOutput,
        Action<string> onError,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        return await RunCoreAsync(startInfo, onOutput, onError, cancellationToken, environment).ConfigureAwait(false);
    }

    public async Task<ProcessResult> RunWithResultAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        Action<string> onOutput,
        Action<string> onError,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return await RunCoreAsync(startInfo, onOutput, onError, cancellationToken, environment).ConfigureAwait(false);
    }

    private async Task<ProcessResult> RunCoreAsync(
        ProcessStartInfo startInfo,
        Action<string> onOutput,
        Action<string> onError,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? environment)
    {
        if (environment is not null)
        {
            foreach (var item in environment)
                startInfo.Environment[item.Key] = item.Value;
        }

        var tcs = new TaskCompletionSource<int>();
        var tracked = await spawner.StartAsync(startInfo, "ProcessHost", cancellationToken).ConfigureAwait(false);
        var process = tracked.Process;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) onOutput(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) onError(e.Data);
        };

        process.Exited += (_, _) => tcs.TrySetResult(process.ExitCode);

        await using var _ = cancellationToken.Register(() =>
        {
            tracked.KillTree();
            tcs.TrySetCanceled(cancellationToken);
        });

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            var exitCode = await tcs.Task.ConfigureAwait(false);
            return new ProcessResult(exitCode);
        }
        finally
        {
            await tracked.DisposeAsync().ConfigureAwait(false);
        }
    }
}
