using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Infrastructure;

namespace Fluence.Infrastructure;

public sealed class TerminalService : ITerminalService, IAsyncDisposable
{
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly StringBuilder _pendingInput = new();
    private Process? _activeProcess;
    private CancellationTokenSource? _activeProcessCts;
    private bool _disposed;

    public event EventHandler<TerminalLineEventArgs>? LineReceived;
    public event EventHandler? Cleared;

    public bool IsBusy => _activeProcess is { HasExited: false };
    public bool HasActiveSession => IsBusy;
    public IPtySession? ActiveSession => null;

    public Task StartShellAsync(string? workingDirectory = null, int columns = 80, int rows = 24, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Task.CompletedTask;
    }

    public async Task SendInputAsync(string text, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (text == "\x03")
        {
            await CancelAsync().ConfigureAwait(false);
            return;
        }

        foreach (var ch in text)
        {
            if (ch is '\r' or '\n')
            {
                var command = _pendingInput.ToString();
                _pendingInput.Clear();
                await ExecuteAsync(command, cancellationToken: cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (ch is '\b' or '\x7F')
            {
                if (_pendingInput.Length > 0)
                {
                    _pendingInput.Length--;
                }

                continue;
            }

            _pendingInput.Append(ch);
        }
    }

    public Task ResizeAsync(int columns, int rows) => Task.CompletedTask;

    public async Task ExecuteAsync(
        string command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        command = command.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        if (IsClearCommand(command))
        {
            Clear();
            return;
        }

        await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var startInfo = CreateShellStartInfo(command, workingDirectory);
            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var exitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            process.OutputDataReceived += (_, e) => PublishLine(e.Data, isError: false);
            process.ErrorDataReceived += (_, e) => PublishLine(e.Data, isError: true);
            process.Exited += (_, _) => exitTcs.TrySetResult(process.ExitCode);

            using var registration = linkedCts.Token.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }

                exitTcs.TrySetCanceled(linkedCts.Token);
            });

            _activeProcessCts = linkedCts;

            PublishLine($"> {command}", isError: false);

            try
            {
                process.Start();
                _activeProcess = process;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var exitCode = await exitTcs.Task.ConfigureAwait(false);
                process.WaitForExit();
                if (exitCode != 0)
                {
                    PublishLine($"[process exited with code {exitCode}]", isError: true);
                }
            }
            catch (OperationCanceledException)
            {
                PublishLine("[process cancelled]", isError: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                PublishLine($"[failed to execute command: {ex.Message}]", isError: true);
            }
            finally
            {
                _activeProcess = null;
                _activeProcessCts = null;
            }
        }
        finally
        {
            _executionGate.Release();
        }
    }

    public Task CancelAsync()
    {
        var process = _activeProcess;
        var cts = _activeProcessCts;
        if (process is null || process.HasExited)
        {
            return Task.CompletedTask;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException) { }

        return Task.CompletedTask;
    }

    public void Clear()
    {
        Cleared?.Invoke(this, EventArgs.Empty);
    }

    private void PublishLine(string? raw, bool isError)
    {
        if (raw is null)
        {
            return;
        }

        var clean = AnsiTextSanitizer.Clean(raw);

        foreach (var line in clean.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            LineReceived?.Invoke(this, new TerminalLineEventArgs(line, isError));
        }
    }

    private static bool IsClearCommand(string command)
    {
        return string.Equals(command, "clear", StringComparison.OrdinalIgnoreCase)
               || string.Equals(command, "cls", StringComparison.OrdinalIgnoreCase);
    }

    private static ProcessStartInfo CreateShellStartInfo(string command, string? workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = GetWorkingDirectory(workingDirectory),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            startInfo.FileName = "pwsh";
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add(command);
        }
        else
        {
            startInfo.FileName = GetUnixShell();
            startInfo.ArgumentList.Add("-lc");
            startInfo.ArgumentList.Add(command);
        }

        return startInfo;
    }

    private static string GetWorkingDirectory(string? workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            return workingDirectory;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static string GetUnixShell()
    {
        if (File.Exists("/bin/zsh"))
        {
            return "/bin/zsh";
        }

        if (File.Exists("/bin/bash"))
        {
            return "/bin/bash";
        }

        return "/bin/sh";
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await CancelAsync().ConfigureAwait(false);

        _disposed = true;

        await _executionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _activeProcess?.Dispose();
            _activeProcessCts?.Dispose();
        }
        finally
        {
            _executionGate.Release();
            _executionGate.Dispose();
        }
    }

    private static class AnsiTextSanitizer
    {
        private static readonly Regex AnsiPattern = new(
            @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~]|\][^\a]*(?:\a|\x1B\\))",
            RegexOptions.Compiled);

        public static string Clean(string value)
        {
            var withoutAnsi = AnsiPattern.Replace(value, string.Empty);
            var builder = new StringBuilder(withoutAnsi.Length);

            foreach (var ch in withoutAnsi)
            {
                if (ch is '\r' or '\n' or '\t' || !char.IsControl(ch))
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }
    }
}
