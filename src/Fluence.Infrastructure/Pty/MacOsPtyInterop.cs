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
        // sizes. posix_spawnattr_t and posix_spawn_file_actions_t are opaque macOS structs, so the
        // buffers stay deliberately oversized and pinned for the duration of this call.
        const int BufferSize = 4096;
        var fileActionsBytes  = new byte[BufferSize];
        var spawnAttrBytes    = new byte[BufferSize];
        var fileActionsHandle = GCHandle.Alloc(fileActionsBytes, GCHandleType.Pinned);
        var spawnAttrHandle   = GCHandle.Alloc(spawnAttrBytes,   GCHandleType.Pinned);
        var fileActions       = fileActionsHandle.AddrOfPinnedObject();
        var spawnAttr         = spawnAttrHandle.AddrOfPinnedObject();
        using var nativeExecutable = new NativeUtf8String(executable);
        using var nativeSlavePath = new NativeUtf8String(slavePath);
        using var nativeWorkingDirectory = string.IsNullOrEmpty(workingDirectory)
            ? null
            : new NativeUtf8String(workingDirectory);
        using var nativeArgv = new NativeStringArray(argv);
        using var nativeEnv = new NativeStringArray(env);

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
            rc = posix_spawn_file_actions_addopen(fileActions, 0, nativeSlavePath.Pointer, O_RDWR, 0);
            if (rc != 0) throw new InvalidOperationException($"posix_spawn_file_actions_addopen failed: errno={rc}");

            rc = posix_spawn_file_actions_adddup2(fileActions, 0, 1);  // stdout = FD 0
            if (rc != 0) throw new InvalidOperationException($"posix_spawn_file_actions_adddup2 stdout failed: errno={rc}");

            rc = posix_spawn_file_actions_adddup2(fileActions, 0, 2);  // stderr = FD 0
            if (rc != 0) throw new InvalidOperationException($"posix_spawn_file_actions_adddup2 stderr failed: errno={rc}");

            // Working directory via macOS-extension file action (macOS 10.15+)
            if (nativeWorkingDirectory is not null)
            {
                rc = posix_spawn_file_actions_addchdir_np(fileActions, nativeWorkingDirectory.Pointer);
                if (rc != 0) throw new InvalidOperationException($"posix_spawn_file_actions_addchdir_np failed: errno={rc}");
            }

            // Spawn attributes
            short flags = (short)(POSIX_SPAWN_SETSID |          // new session (no controlling terminal yet)
                                  POSIX_SPAWN_SETSIGDEF |        // reset signal handlers to SIG_DFL
                                  POSIX_SPAWN_SETSIGMASK |       // set signal mask to empty
                                  POSIX_SPAWN_CLOEXEC_DEFAULT);  // close all inherited FDs at exec
            rc = posix_spawnattr_setflags(spawnAttr, flags);
            if (rc != 0) throw new InvalidOperationException($"posix_spawnattr_setflags failed: errno={rc}");

            uint allSignals = uint.MaxValue;  // all signals → SIG_DFL
            rc = posix_spawnattr_setsigdefault(spawnAttr, ref allSignals);
            if (rc != 0) throw new InvalidOperationException($"posix_spawnattr_setsigdefault failed: errno={rc}");

            uint noSignals = 0u;              // unblock all signals in child
            rc = posix_spawnattr_setsigmask(spawnAttr, ref noSignals);
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
                    fileActions,
                    spawnAttr,
                    nativeArgv.Pointer,
                    nativeEnv.Pointer);
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
