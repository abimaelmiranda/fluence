using System;
using System.IO;
using System.Runtime.InteropServices;
using Fluence.Core.Abstractions.Infrastructure;
using Porta.Pty;

namespace Fluence.Infrastructure.Pty;

internal sealed class PortaPtySession : IPtySession
{
    private const int SigHup = 1;
    private const int SigTerm = 15;
    private const int SigKill = 9;
    private const int ESRCH = 3;
    private const int GracefulExitMs = 250;
    private const int ForcedExitMs = 250;

    private readonly IPtyConnection _pty;
    private volatile bool _hasExited;

    public PortaPtySession(IPtyConnection pty)
    {
        _pty = pty;
        _pty.ProcessExited += OnProcessExited;
    }

    public Stream Input => _pty.WriterStream;
    public Stream Output => _pty.ReaderStream;
    public bool HasExited => _hasExited;
    public event EventHandler? Exited;

    public void Resize(int columns, int rows) => _pty.Resize(columns, rows);

    private void OnProcessExited(object? sender, PtyExitedEventArgs e)
    {
        _hasExited = true;
        Exited?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _pty.ProcessExited -= OnProcessExited;
        TryKillUnixProcessGroup();
        try { _pty.Kill(); } catch { }
        _pty.Dispose();
    }

    private void TryKillUnixProcessGroup()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            return;

        var pid = _pty.Pid;
        if (pid <= 0 || !IsProcessGroupLeader(pid))
            return;

        // forkpty/login_tty children are expected to be session/process-group leaders.
        // Verify before using negative pid so we never signal an unrelated process group.
        KillNoThrow(-pid, SigHup);
        if (WaitForExitNoThrow(GracefulExitMs))
            return;

        KillNoThrow(-pid, SigTerm);
        if (WaitForExitNoThrow(ForcedExitMs))
            return;

        KillNoThrow(-pid, SigKill);
    }

    private bool WaitForExitNoThrow(int milliseconds)
    {
        try { return _pty.WaitForExit(milliseconds); }
        catch { return false; }
    }

    private static bool IsProcessGroupLeader(int pid)
    {
        try { return getpgid(pid) == pid; }
        catch { return false; }
    }

    private static void KillNoThrow(int pid, int signal)
    {
        if (kill(pid, signal) == 0)
            return;

        _ = Marshal.GetLastPInvokeError() == ESRCH;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int getpgid(int pid);

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);
}
