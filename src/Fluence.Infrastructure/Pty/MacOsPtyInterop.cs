
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Fluence.Infrastructure.Pty;

internal static class MacOsPtyInterop
{
    private const string Libc = "libc";

    [DllImport(Libc, SetLastError = true)]
    private static extern int openpty(
        out int amaster,
        out int aslave,
        IntPtr name,
        IntPtr termp,
        ref WinSize winp);

    [DllImport(Libc, SetLastError = true)]
    private static extern int fork();

    [DllImport(Libc, SetLastError = true)]
    private static extern int setsid();

    [DllImport(Libc, SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, ref WinSize data);

    [DllImport(Libc, SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, int data);

    [DllImport(Libc, SetLastError = true)]
    private static extern int dup2(int oldfd, int newfd);

    [DllImport(Libc, SetLastError = true)]
    private static extern int close(int fd);

    [DllImport(Libc, SetLastError = true)]
    private static extern int execve(string path, string[] argv, string[] envp);

    [DllImport(Libc, SetLastError = true)]
    private static extern int waitpid(int pid, out int status, int options);

    [DllImport(Libc, SetLastError = true)]
    private static extern int kill(int pid, int sig);

    [DllImport(Libc, SetLastError = true)]
    private static extern int chdir(string path);

    // TIOCSWINSZ on macOS/arm64 and x64
    private const ulong TiocsWinsz = 0x80087467;
    private const ulong Tiocsctty = 0x20007461;
    private const int Sigterm = 15;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WinSize
    {
        public ushort ws_row;
        public ushort ws_col;
        public ushort ws_xpixel;
        public ushort ws_ypixel;
    }

    public static (int masterFd, int childPid) Spawn(
        string executable,
        string[] argv,
        string workingDirectory,
        string[] env,
        int columns,
        int rows)
    {
        var winSize = new WinSize { ws_col = (ushort)columns, ws_row = (ushort)rows };

        if (openpty(out int master, out int slave, IntPtr.Zero, IntPtr.Zero, ref winSize) != 0)
        {
            throw new InvalidOperationException($"openpty failed errno={Marshal.GetLastPInvokeError()}");
        }

        int pid = fork();
        if (pid < 0)
        {
            close(master);
            close(slave);
            throw new InvalidOperationException($"fork failed errno={Marshal.GetLastPInvokeError()}");
        }

        if (pid == 0)
        {
            // Child: become the session leader and attach slave PTY to stdio
            setsid();
            ioctl(slave, Tiocsctty, 0);
            dup2(slave, 0);
            dup2(slave, 1);
            dup2(slave, 2);
            close(master);
            close(slave);

            if (!string.IsNullOrEmpty(workingDirectory))
                chdir(workingDirectory); // pure syscall — safe after fork()

            execve(executable, argv, env);
            Environment.Exit(127);
        }

        // Parent: close slave end
        close(slave);
        return (master, pid);
    }

    public static void SetWinSize(int masterFd, int columns, int rows)
    {
        var ws = new WinSize { ws_col = (ushort)columns, ws_row = (ushort)rows };
        ioctl(masterFd, TiocsWinsz, ref ws);
    }

    public static void WaitPid(int pid)
    {
        waitpid(pid, out _, 0);
    }

    public static void Kill(int pid)
    {
        kill(pid, Sigterm);
    }

    public static void CloseFd(int fd)
    {
        close(fd);
    }

    public static string[] BuildEnvironment()
    {
        var env = new List<string>();
        bool hasTerm = false;

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString() ?? string.Empty;
            env.Add($"{key}={entry.Value}");
            if (key.Equals("TERM", StringComparison.Ordinal)) hasTerm = true;
        }

        if (!hasTerm)
        {
            env.Add("TERM=xterm-256color");
        }

        return env.ToArray();
    }
}
