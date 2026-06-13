using System;
using System.IO;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Fluence.Core.Infrastructure;

namespace Fluence.Infrastructure.Pty;

internal sealed class MacOsPtySession : IPtySession
{
    private static readonly TimeSpan WatcherJoinTimeout = TimeSpan.FromMilliseconds(250);

    private readonly int _masterFd;
    private readonly int _childPid;
    private readonly FileStream _reader;
    private readonly FileStream _writer;
    private readonly Thread _watcherThread;
    private int _disposed;
    private int _exitNotified;
    private volatile bool _hasExited;

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

        _watcherThread = new Thread(WatchChildProc)
        {
            IsBackground = true,
            Name = $"pty-watcher-{childPid}",
        };
        _watcherThread.Start();
    }

    public Stream Input { get; }
    public Stream Output { get; }
    public bool HasExited => _hasExited;
    public event EventHandler? Exited;

    public void Resize(int columns, int rows)
    {
        if (Volatile.Read(ref _disposed) == 0)
            MacOsPtyInterop.SetWinSize(_masterFd, columns, rows);
    }

    private void WatchChildProc()
    {
        try
        {
            MacOsPtyInterop.WaitPid(_childPid);
        }
        catch
        {
            // Exit observation is best-effort; disposal still closes the PTY fd.
        }
        finally
        {
            _hasExited = true;
            if (Volatile.Read(ref _disposed) == 0)
                NotifyExited();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        _hasExited = true;

        try { MacOsPtyInterop.KillProcessGroup(_childPid); } catch { }
        try { MacOsPtyInterop.Kill(_childPid); } catch { }
        try { _reader.Dispose(); } catch { }
        try { _writer.Dispose(); } catch { }
        try { MacOsPtyInterop.CloseFd(_masterFd); } catch { } // close real fd (ownsHandle: false on both streams)

        if (Thread.CurrentThread != _watcherThread)
        {
            try { _watcherThread.Join(WatcherJoinTimeout); } catch { }
        }
    }

    private void NotifyExited()
    {
        if (Interlocked.Exchange(ref _exitNotified, 1) == 0)
            Exited?.Invoke(this, EventArgs.Empty);
    }
}
