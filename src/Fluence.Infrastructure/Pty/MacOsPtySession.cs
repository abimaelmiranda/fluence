using System;
using System.IO;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Fluence.Core.Infrastructure;

namespace Fluence.Infrastructure.Pty;

internal sealed class MacOsPtySession : IPtySession
{
    private readonly int _masterFd;
    private readonly int _childPid;
    private readonly FileStream _reader;
    private readonly FileStream _writer;
    private bool _disposed;

    public MacOsPtySession(int masterFd, int childPid, int columns, int rows)
    {
        _masterFd = masterFd;
        _childPid = childPid;

        // Two separate streams pointing at the same fd (ownsHandle: false on both).
        // The fd is closed explicitly in Dispose(). This is the vs-pty.net pattern —
        // a single FileStream shared between read and write threads causes concurrent-
        // access corruption because FileStream is not thread-safe.
        _reader = new FileStream(
            new SafeFileHandle(new IntPtr(masterFd), ownsHandle: false),
            FileAccess.Read, bufferSize: 1024, isAsync: false);

        _writer = new FileStream(
            new SafeFileHandle(new IntPtr(masterFd), ownsHandle: false),
            FileAccess.Write, bufferSize: 1024, isAsync: false);

        Input = _writer;
        Output = _reader;

        var watcher = new Thread(WatchChildProc)
        {
            IsBackground = true,
            Name = $"pty-watcher-{childPid}",
        };
        watcher.Start();
    }

    public Stream Input { get; }
    public Stream Output { get; }
    public bool HasExited { get; private set; }
    public event EventHandler? Exited;

    public void Resize(int columns, int rows)
    {
        if (!_disposed)
            MacOsPtyInterop.SetWinSize(_masterFd, columns, rows);
    }

    private void WatchChildProc()
    {
        MacOsPtyInterop.WaitPid(_childPid);
        HasExited = true;
        if (!_disposed)
            Exited?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        HasExited = true;

        try { MacOsPtyInterop.Kill(_childPid); } catch { }
        try { _reader.Dispose(); } catch { }
        try { _writer.Dispose(); } catch { }
        MacOsPtyInterop.CloseFd(_masterFd); // close real fd (ownsHandle: false on both streams)
    }
}
