using System;
using System.IO;
using Microsoft.Win32.SafeHandles;
using Fluence.Core.Infrastructure;

namespace Fluence.Infrastructure.Pty;

internal sealed class MacOsPtySession : IPtySession
{
    private readonly int _masterFd;
    private readonly int _childPid;
    private readonly FileStream _stream;
    private readonly System.Threading.Tasks.Task _exitTask;
    private bool _disposed;

    public MacOsPtySession(int masterFd, int childPid, int columns, int rows)
    {
        _masterFd = masterFd;
        _childPid = childPid;

        // The PTY master fd is bidirectional — wrap it in a single R/W FileStream.
        // ownsHandle: true so the fd is closed when the stream is disposed.
        var handle = new SafeFileHandle(new IntPtr(masterFd), ownsHandle: true);
        // PTY fds on macOS/Linux don't support overlapped I/O — must use synchronous mode.
        // TerminalService wraps reads in Task.Run to avoid blocking the UI thread.
        _stream = new FileStream(handle, FileAccess.ReadWrite, bufferSize: 4096, isAsync: false);

        Input = _stream;
        Output = _stream;

        _exitTask = WatchExitAsync();
    }

    public Stream Input { get; }
    public Stream Output { get; }
    public bool HasExited { get; private set; }
    public event EventHandler? Exited;

    public void Resize(int columns, int rows)
    {
        if (!_disposed)
        {
            MacOsPtyInterop.SetWinSize(_masterFd, columns, rows);
        }
    }

    private async System.Threading.Tasks.Task WatchExitAsync()
    {
        await System.Threading.Tasks.Task.Run(() => MacOsPtyInterop.WaitPid(_childPid));
        HasExited = true;
        if (!_disposed)
        {
            Exited?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        HasExited = true;

        try { MacOsPtyInterop.Kill(_childPid); } catch { }
        _stream.Dispose(); // closes the SafeFileHandle which closes the fd
        _ = _exitTask;
    }
}
