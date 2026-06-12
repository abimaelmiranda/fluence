using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Infrastructure;

namespace Fluence.Infrastructure;

public sealed class TerminalService : ITerminalService, IAsyncDisposable
{
    private readonly IPtyHost _ptyHost;
    private IPtySession? _session;
    private CancellationTokenSource? _readCts;
    private string? _shellWorkingDirectory;
    private bool _disposed;

    public TerminalService(IPtyHost ptyHost)
    {
        _ptyHost = ptyHost;
    }

    public event EventHandler<TerminalDataEventArgs>? DataReceived;
    public event EventHandler? Cleared;

    public bool IsBusy => HasActiveSession;
    public bool HasActiveSession => _session is { HasExited: false };
    public IPtySession? ActiveSession => _session;

    public Task StartShellAsync(
        string? workingDirectory = null,
        int columns = 80,
        int rows = 24,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (HasActiveSession)
            return Task.CompletedTask;

        var dir = ResolveDirectory(workingDirectory);
        _shellWorkingDirectory = dir;

        _readCts?.Cancel();
        _readCts = new CancellationTokenSource();
        var ct = _readCts.Token;

        _session = _ptyHost.CreateShellSession(dir, columns, rows);
        _session.Exited += OnSessionExited;

        StartReadLoop(_session, ct);
        return Task.CompletedTask;
    }

    public async Task SendInputAsync(string text, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_session is null || _session.HasExited)
            return;

        var bytes = Encoding.UTF8.GetBytes(text);
        await Task.Run(() =>
        {
            try
            {
                _session.Input.Write(bytes, 0, bytes.Length);
                _session.Input.Flush();
            }
            catch (IOException) { }
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task ResizeAsync(int columns, int rows)
    {
        _session?.Resize(columns, rows);
        return Task.CompletedTask;
    }

    public async Task ExecuteAsync(
        string command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        command = command.Trim();
        if (string.IsNullOrWhiteSpace(command))
            return;

        if (!HasActiveSession)
            await StartShellAsync(workingDirectory, cancellationToken: cancellationToken).ConfigureAwait(false);

        string toSend;
        if (!string.IsNullOrWhiteSpace(workingDirectory) &&
            !string.Equals(workingDirectory, _shellWorkingDirectory, StringComparison.OrdinalIgnoreCase))
        {
            toSend = $"cd {QuoteShellPath(workingDirectory)} && {command}\r";
        }
        else
        {
            toSend = command + "\r";
        }

        await SendInputAsync(toSend, cancellationToken).ConfigureAwait(false);
    }

    public Task CancelAsync()
    {
        return SendInputAsync("\x03");
    }

    public void Clear()
    {
        Cleared?.Invoke(this, EventArgs.Empty);
    }

    private void StartReadLoop(IPtySession session, CancellationToken ct)
    {
        var t = new System.Threading.Thread(() => ReadThreadProc(session, ct))
        {
            IsBackground = true,
            Name = "pty-read-loop",
        };
        t.Start();
    }

    private void ReadThreadProc(IPtySession session, CancellationToken ct)
    {
        var buf = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested && !session.HasExited)
            {
                int n;
                try { n = session.Output.Read(buf, 0, buf.Length); }
                catch (IOException) { break; }

                if (n <= 0) break;

                var chunk = new byte[n];
                Buffer.BlockCopy(buf, 0, chunk, 0, n);
                DataReceived?.Invoke(this, new TerminalDataEventArgs(chunk));
            }
        }
        catch when (ct.IsCancellationRequested) { }
    }

    private void OnSessionExited(object? sender, EventArgs e)
    {
        _readCts?.Cancel();
    }

    private static string ResolveDirectory(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            return path;
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static string QuoteShellPath(string path)
    {
        // Single-quote the path and escape embedded single-quotes for POSIX shells
        return "'" + path.Replace("'", "'\\''") + "'";
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _readCts?.Cancel();
        _readCts?.Dispose();
        _readCts = null;
        if (_session is not null)
        {
            _session.Exited -= OnSessionExited;
            await Task.Run(() => _session.Dispose()).ConfigureAwait(false);
            _session = null;
        }
    }
}
