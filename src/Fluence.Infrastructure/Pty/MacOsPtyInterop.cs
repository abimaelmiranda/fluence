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
        nint path,
        nint fileActions,
        nint attr,
        nint argv,
        nint envp);

    [DllImport(Libc)]
    private static extern int posix_spawn_file_actions_init(nint fileActions);

    [DllImport(Libc)]
    private static extern int posix_spawn_file_actions_destroy(nint fileActions);

    [DllImport(Libc)]
    private static extern int posix_spawn_file_actions_addopen(
        nint fileActions, int fd, nint path, int flags, int mode);

    [DllImport(Libc)]
    private static extern int posix_spawn_file_actions_adddup2(
        nint fileActions, int fd, int newfd);

    [DllImport(Libc)]
    private static extern int posix_spawn_file_actions_addclose(
        nint fileActions, int fd);

    // macOS extension (10.15+): set working directory for child
    [DllImport(Libc)]
    private static extern int posix_spawn_file_actions_addchdir_np(
        nint fileActions, nint path);

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
    private const int EINTR = 4;
    private const int ECHILD = 10;
    private const int ESRCH = 3;

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

        using var fileActions = new PosixSpawnFileActions();
        using var spawnAttr = new PosixSpawnAttributes();
        using var nativeExecutable = new NativeUtf8String(executable);
        using var nativeSlavePath = new NativeUtf8String(slavePath);
        using var nativeWorkingDirectory = string.IsNullOrEmpty(workingDirectory)
            ? null
            : new NativeUtf8String(workingDirectory);
        using var nativeArgv = new NativeStringArray(argv);
        using var nativeEnv = new NativeStringArray(env);

        try
        {
            fileActions.Initialize();
            spawnAttr.Initialize();

            // stdin: open slave by path — as session leader (POSIX_SPAWN_SETSID), opening a terminal
            // device without O_NOCTTY automatically sets it as the controlling terminal.
            int rc = posix_spawn_file_actions_addopen(fileActions.Pointer, 0, nativeSlavePath.Pointer, O_RDWR, 0);
            if (rc != 0) throw new InvalidOperationException($"posix_spawn_file_actions_addopen failed: errno={rc}");

            rc = posix_spawn_file_actions_adddup2(fileActions.Pointer, 0, 1);  // stdout = FD 0
            if (rc != 0) throw new InvalidOperationException($"posix_spawn_file_actions_adddup2 stdout failed: errno={rc}");

            rc = posix_spawn_file_actions_adddup2(fileActions.Pointer, 0, 2);  // stderr = FD 0
            if (rc != 0) throw new InvalidOperationException($"posix_spawn_file_actions_adddup2 stderr failed: errno={rc}");

            // Working directory via macOS-extension file action (macOS 10.15+)
            if (nativeWorkingDirectory is not null)
            {
                rc = posix_spawn_file_actions_addchdir_np(fileActions.Pointer, nativeWorkingDirectory.Pointer);
                if (rc != 0) throw new InvalidOperationException($"posix_spawn_file_actions_addchdir_np failed: errno={rc}");
            }

            // Spawn attributes
            short flags = (short)(POSIX_SPAWN_SETSID |          // new session (no controlling terminal yet)
                                  POSIX_SPAWN_SETSIGDEF |        // reset signal handlers to SIG_DFL
                                  POSIX_SPAWN_SETSIGMASK |       // set signal mask to empty
                                  POSIX_SPAWN_CLOEXEC_DEFAULT);  // close all inherited FDs at exec
            rc = posix_spawnattr_setflags(spawnAttr.Pointer, flags);
            if (rc != 0) throw new InvalidOperationException($"posix_spawnattr_setflags failed: errno={rc}");

            uint allSignals = uint.MaxValue;  // all signals → SIG_DFL
            rc = posix_spawnattr_setsigdefault(spawnAttr.Pointer, ref allSignals);
            if (rc != 0) throw new InvalidOperationException($"posix_spawnattr_setsigdefault failed: errno={rc}");

            uint noSignals = 0u;              // unblock all signals in child
            rc = posix_spawnattr_setsigmask(spawnAttr.Pointer, ref noSignals);
            if (rc != 0) throw new InvalidOperationException($"posix_spawnattr_setsigmask failed: errno={rc}");

            // Retry on EAGAIN (errno=11): kernel temporarily can't spawn due to resource pressure.
            // argv/envp are passed as native null-terminated char** arrays. Passing managed
            // string[] here can intermittently surface as EFAULT (errno=14) under churn.
            const int EAGAIN = 11;
            int spawnedPid;
            for (int attempt = 0; ; attempt++)
            {
                rc = posix_spawn(
                    out spawnedPid,
                    nativeExecutable.Pointer,
                    fileActions.Pointer,
                    spawnAttr.Pointer,
                    nativeArgv.Pointer,
                    nativeEnv.Pointer);
                if (rc == 0) break;
                if (rc != EAGAIN || attempt >= 2)
                    throw new InvalidOperationException(
                        $"posix_spawn failed: errno={rc} executable={executable}");
                Thread.Sleep(25 << attempt);  // 25ms, then 50ms
            }

            CloseFdChecked(slave);  // parent holds only master end
            return (master, spawnedPid);
        }
        catch
        {
            CloseFdNoThrow(slave);
            CloseFdNoThrow(master);
            throw;
        }
    }

    public static bool SetWinSize(int masterFd, int columns, int rows)
    {
        var ws = new WinSize { ws_col = (ushort)columns, ws_row = (ushort)rows };
        return ioctl(masterFd, TiocsWinsz, ref ws) == 0;
    }

    public static bool WaitPid(int pid)
    {
        while (true)
        {
            var result = waitpid(pid, out _, 0);
            if (result == pid)
                return true;

            if (result != -1)
                continue;

            var errno = Marshal.GetLastPInvokeError();
            if (errno == EINTR)
                continue;
            if (errno == ECHILD)
                return false;

            throw new InvalidOperationException($"waitpid failed: errno={errno} pid={pid}");
        }
    }

    public static void Kill(int pid)
    {
        KillChecked(pid);
    }

    public static void KillProcessGroup(int pid)
    {
        KillChecked(-pid);
    }

    public static void CloseFd(int fd)
    {
        CloseFdChecked(fd);
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

    private static void KillChecked(int pid)
    {
        if (kill(pid, Sigterm) == 0)
            return;

        var errno = Marshal.GetLastPInvokeError();
        if (errno == ESRCH)
            return;

        throw new InvalidOperationException($"kill failed: errno={errno} pid={pid}");
    }

    private static void CloseFdChecked(int fd)
    {
        if (close(fd) == 0)
            return;

        var errno = Marshal.GetLastPInvokeError();
        if (errno == EINTR)
            return;

        throw new InvalidOperationException($"close failed: errno={errno} fd={fd}");
    }

    private static void CloseFdNoThrow(int fd)
    {
        try { CloseFdChecked(fd); } catch { }
    }

    private sealed class PosixSpawnFileActions : IDisposable
    {
        private bool _initialized;

        public PosixSpawnFileActions()
        {
            Pointer = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(Pointer, IntPtr.Zero);
        }

        public nint Pointer { get; private set; }

        public void Initialize()
        {
            var rc = posix_spawn_file_actions_init(Pointer);
            if (rc != 0)
                throw new InvalidOperationException($"posix_spawn_file_actions_init failed: errno={rc}");

            _initialized = true;
        }

        public void Dispose()
        {
            if (Pointer == IntPtr.Zero)
                return;

            if (_initialized)
                posix_spawn_file_actions_destroy(Pointer);

            Marshal.FreeHGlobal(Pointer);
            Pointer = IntPtr.Zero;
        }
    }

    private sealed class PosixSpawnAttributes : IDisposable
    {
        private bool _initialized;

        public PosixSpawnAttributes()
        {
            Pointer = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(Pointer, IntPtr.Zero);
        }

        public nint Pointer { get; private set; }

        public void Initialize()
        {
            var rc = posix_spawnattr_init(Pointer);
            if (rc != 0)
                throw new InvalidOperationException($"posix_spawnattr_init failed: errno={rc}");

            _initialized = true;
        }

        public void Dispose()
        {
            if (Pointer == IntPtr.Zero)
                return;

            if (_initialized)
                posix_spawnattr_destroy(Pointer);

            Marshal.FreeHGlobal(Pointer);
            Pointer = IntPtr.Zero;
        }
    }

    private sealed class NativeUtf8String : IDisposable
    {
        public NativeUtf8String(string value)
        {
            if (value.IndexOf('\0') >= 0)
                throw new ArgumentException("Native strings cannot contain null characters.", nameof(value));

            var bytes = Encoding.UTF8.GetBytes(value + '\0');
            Pointer = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, Pointer, bytes.Length);
        }

        public nint Pointer { get; private set; }

        public void Dispose()
        {
            if (Pointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Pointer);
                Pointer = IntPtr.Zero;
            }
        }
    }

    private sealed class NativeStringArray : IDisposable
    {
        private readonly nint[] _strings;

        public NativeStringArray(IReadOnlyList<string> values)
        {
            _strings = new nint[values.Count];

            try
            {
                Pointer = Marshal.AllocHGlobal((values.Count + 1) * IntPtr.Size);
                for (var i = 0; i < values.Count; i++)
                {
                    _strings[i] = AllocateUtf8String(values[i]);
                    Marshal.WriteIntPtr(Pointer, i * IntPtr.Size, _strings[i]);
                }

                Marshal.WriteIntPtr(Pointer, values.Count * IntPtr.Size, IntPtr.Zero);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public nint Pointer { get; private set; }

        public void Dispose()
        {
            for (var i = 0; i < _strings.Length; i++)
            {
                var ptr = _strings[i];
                if (ptr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(ptr);
                    _strings[i] = IntPtr.Zero;
                }
            }

            if (Pointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(Pointer);
                Pointer = IntPtr.Zero;
            }
        }

        private static nint AllocateUtf8String(string value)
        {
            if (value.IndexOf('\0') >= 0)
                throw new ArgumentException("Native strings cannot contain null characters.", nameof(value));

            var bytes = Encoding.UTF8.GetBytes(value + '\0');
            var ptr = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            return ptr;
        }
    }
}
