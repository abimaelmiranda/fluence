using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Fluence.Infrastructure.Pty;

internal static class MacOsPtyInterop
{
    private const string Libc = "libc";

    // name is byte[256]: openpty writes the slave path there directly.
    // Using a caller-owned buffer avoids the ptsname(3) static-buffer race when
    // multiple sessions are spawned concurrently from different thread-pool threads.
    [DllImport(Libc, SetLastError = true)]
    private static extern int openpty(
        out int amaster,
        out int aslave,
        byte[]? name,
        IntPtr termp,
        ref WinSize winp);

    [DllImport(Libc, SetLastError = true)]
    private static extern int waitpid(int pid, out int status, int options);

    [DllImport(Libc, SetLastError = true)]
    private static extern int kill(int pid, int sig);

    [DllImport(Libc, SetLastError = true)]
    private static extern int close(int fd);

    [DllImport(Libc, SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, ref WinSize data);

    // posix_spawn returns errno on failure (0 on success) — do not use SetLastError
    [DllImport(Libc)]
    private static extern int posix_spawn(
        out int pid,
        string path,
        nint fileActions,
        nint attr,
        string[] argv,
        string[] envp);

    [DllImport(Libc)]
    private static extern int posix_spawn_file_actions_init(nint fileActions);

    [DllImport(Libc)]
    private static extern int posix_spawn_file_actions_destroy(nint fileActions);

    [DllImport(Libc)]
    private static extern int posix_spawn_file_actions_addopen(
        nint fileActions, int fd, string path, int flags, int mode);

    [DllImport(Libc)]
    private static extern int posix_spawn_file_actions_adddup2(
        nint fileActions, int fd, int newfd);

    [DllImport(Libc)]
    private static extern int posix_spawn_file_actions_addclose(
        nint fileActions, int fd);

    // macOS extension (10.15+): set working directory for child
    [DllImport(Libc)]
    private static extern int posix_spawn_file_actions_addchdir_np(
        nint fileActions, string path);

    [DllImport(Libc)]
    private static extern int posix_spawnattr_init(nint attr);

    [DllImport(Libc)]
    private static extern int posix_spawnattr_destroy(nint attr);

    [DllImport(Libc)]
    private static extern int posix_spawnattr_setflags(nint attr, short flags);

    // On macOS, sigset_t is uint (32-bit unsigned int)
    [DllImport(Libc)]
    private static extern int posix_spawnattr_setsigdefault(nint attr, ref uint sigdefault);

    [DllImport(Libc)]
    private static extern int posix_spawnattr_setsigmask(nint attr, ref uint sigmask);

    private const ulong TiocsWinsz = 0x80087467;
    private const int Sigterm = 15;
    private const int O_RDWR  = 2;

    // posix_spawn flags (macOS spawn.h)
    private const short POSIX_SPAWN_SETSIGDEF       = 0x0004;  // reset signals to SIG_DFL
    private const short POSIX_SPAWN_SETSIGMASK      = 0x0008;  // set signal mask
    private const short POSIX_SPAWN_SETSID          = 0x0400;  // new session (macOS 10.15+)
    private const short POSIX_SPAWN_CLOEXEC_DEFAULT = 0x4000;  // close all inherited FDs (macOS)

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

        // openpty writes the slave path into nameBuffer — each call gets its own buffer,
        // eliminating the ptsname(3) static-buffer race on concurrent Spawn() calls.
        var nameBuffer = new byte[256];
        if (openpty(out int master, out int slave, nameBuffer, IntPtr.Zero, ref winSize) != 0)
            throw new InvalidOperationException($"openpty failed: errno={Marshal.GetLastPInvokeError()}");

        var nullIdx = Array.IndexOf(nameBuffer, (byte)0);
        var slavePath = nullIdx > 0
            ? Encoding.ASCII.GetString(nameBuffer, 0, nullIdx)
            : throw new InvalidOperationException("openpty returned empty slave path");

        // Use GCHandle-pinned managed byte arrays instead of Marshal.AllocHGlobal with hardcoded
        // sizes.  AllocHGlobal(256/512) overflows if the macOS internal struct layout grows across
        // OS versions, silently corrupting the heap and producing EFAULT on the next posix_spawn
        // call.  4 KB each is orders-of-magnitude larger than any real posix_spawn struct; the GC
        // zero-initialises the array and keeps it pinned for the duration of this call.
        const int BufferSize = 4096;
        var fileActionsBytes  = new byte[BufferSize];
        var spawnAttrBytes    = new byte[BufferSize];
        var fileActionsHandle = GCHandle.Alloc(fileActionsBytes, GCHandleType.Pinned);
        var spawnAttrHandle   = GCHandle.Alloc(spawnAttrBytes,   GCHandleType.Pinned);
        var fileActions       = fileActionsHandle.AddrOfPinnedObject();
        var spawnAttr         = spawnAttrHandle.AddrOfPinnedObject();

        bool fileActionsInited = false;
        bool spawnAttrInited   = false;

        try
        {
            int rc = posix_spawn_file_actions_init(fileActions);
            if (rc != 0) throw new InvalidOperationException($"posix_spawn_file_actions_init failed: errno={rc}");
            fileActionsInited = true;

            rc = posix_spawnattr_init(spawnAttr);
            if (rc != 0) throw new InvalidOperationException($"posix_spawnattr_init failed: errno={rc}");
            spawnAttrInited = true;

            // stdin: open slave by path — as session leader (POSIX_SPAWN_SETSID), opening a terminal
            // device without O_NOCTTY automatically sets it as the controlling terminal.
            posix_spawn_file_actions_addopen(fileActions, 0, slavePath, O_RDWR, 0);
            posix_spawn_file_actions_adddup2(fileActions, 0, 1);  // stdout = FD 0
            posix_spawn_file_actions_adddup2(fileActions, 0, 2);  // stderr = FD 0

            // Working directory via macOS-extension file action (macOS 10.15+)
            if (!string.IsNullOrEmpty(workingDirectory))
                posix_spawn_file_actions_addchdir_np(fileActions, workingDirectory);

            // Spawn attributes
            short flags = (short)(POSIX_SPAWN_SETSID |          // new session (no controlling terminal yet)
                                  POSIX_SPAWN_SETSIGDEF |        // reset signal handlers to SIG_DFL
                                  POSIX_SPAWN_SETSIGMASK |       // set signal mask to empty
                                  POSIX_SPAWN_CLOEXEC_DEFAULT);  // close all inherited FDs at exec
            rc = posix_spawnattr_setflags(spawnAttr, flags);
            if (rc != 0) throw new InvalidOperationException($"posix_spawnattr_setflags failed: errno={rc}");

            uint allSignals = uint.MaxValue;  // all signals → SIG_DFL
            posix_spawnattr_setsigdefault(spawnAttr, ref allSignals);

            uint noSignals = 0u;              // unblock all signals in child
            posix_spawnattr_setsigmask(spawnAttr, ref noSignals);

            // Retry on EAGAIN (errno=11): kernel temporarily can't spawn due to resource pressure.
            // TODO: rc=14 (EFAULT) observed intermittently on second session spawn. Root cause not
            // fully confirmed — likely struct-layout overflow from hardcoded AllocHGlobal sizes in
            // the previous implementation; GCHandle+4KB buffers should fix it, but needs validation
            // across more macOS versions. If EFAULT persists, audit fileActions/spawnAttr struct
            // sizes via `sizeof` in native code and match buffers exactly.
            const int EAGAIN = 11;
            int spawnedPid;
            for (int attempt = 0; ; attempt++)
            {
                rc = posix_spawn(out spawnedPid, executable, fileActions, spawnAttr, argv, env);
                if (rc == 0) break;
                if (rc != EAGAIN || attempt >= 2)
                    throw new InvalidOperationException(
                        $"posix_spawn failed: errno={rc} executable={executable}");
                Thread.Sleep(25 << attempt);  // 25ms, then 50ms
            }

            close(slave);  // parent holds only master end
            return (master, spawnedPid);
        }
        catch
        {
            try { close(slave); } catch { }
            try { close(master); } catch { }
            throw;
        }
        finally
        {
            if (fileActionsInited) posix_spawn_file_actions_destroy(fileActions);
            if (spawnAttrInited)   posix_spawnattr_destroy(spawnAttr);
            fileActionsHandle.Free();
            spawnAttrHandle.Free();
        }
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

    public static void KillProcessGroup(int pid)
    {
        kill(-pid, Sigterm);
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
            env.Add("TERM=xterm-256color");

        return env.ToArray();
    }
}
