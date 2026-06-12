using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Infrastructure;

namespace Fluence.Infrastructure;

public sealed class ProcessHost : IProcessHost
{
    public async Task RunAsync(
        string executable,
        string arguments,
        string? workingDirectory,
        Action<string> onOutput,
        Action<string> onError,
        CancellationToken cancellationToken = default)
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

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var tcs = new TaskCompletionSource<int>();

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
            try { process.Kill(entireProcessTree: true); } catch { }
            tcs.TrySetCanceled(cancellationToken);
        });

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await tcs.Task;
    }
}
