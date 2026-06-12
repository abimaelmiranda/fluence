using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Fluence.Core.Infrastructure;

namespace Fluence.Infrastructure.Pty;

public sealed class WindowsPtyHost : IPtyHost
{
    private static readonly string DefaultShell =
        Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";

    public IPtySession CreateSession(
        string executable,
        string arguments,
        string workingDirectory,
        int columns = 80,
        int rows = 24)
    {
        return WindowsPtySession.Create(executable, arguments, workingDirectory, columns, rows);
    }

    public IPtySession CreateShellSession(string workingDirectory, int columns = 80, int rows = 24)
    {
        // Prefer PowerShell 7+ (pwsh); fall back to legacy cmd
        var pwsh = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "PowerShell", "7", "pwsh.exe");
        var (shell, args) = File.Exists(pwsh)
            ? (pwsh, "-NoLogo")
            : (DefaultShell, string.Empty);
        return WindowsPtySession.Create(shell, args, workingDirectory, columns, rows);
    }
}

internal sealed class WindowsPtySession : IPtySession
{
    private readonly SafeFileHandle _hPseudoConsole;
    private readonly AnonymousPipeServerStream _inputPipe;
    private readonly AnonymousPipeServerStream _outputPipe;
    private readonly System.Diagnostics.Process _process;
    private bool _disposed;

    private WindowsPtySession(
        SafeFileHandle hPc,
        AnonymousPipeServerStream input,
        AnonymousPipeServerStream output,
        System.Diagnostics.Process process)
    {
        _hPseudoConsole = hPc;
        _inputPipe = input;
        _outputPipe = output;
        _process = process;
        Input = input;
        Output = output;
        _ = WatchExitAsync();
    }

    public Stream Input { get; }
    public Stream Output { get; }
    public bool HasExited { get; private set; }
    public event EventHandler? Exited;

    public void Resize(int columns, int rows)
    {
        if (!_disposed)
        {
            var size = new COORD { X = (short)columns, Y = (short)rows };
            ResizePseudoConsole(_hPseudoConsole, size);
        }
    }

    private async System.Threading.Tasks.Task WatchExitAsync()
    {
        await _process.WaitForExitAsync();
        HasExited = true;
        Exited?.Invoke(this, EventArgs.Empty);
    }

    public static WindowsPtySession Create(
        string executable,
        string arguments,
        string workingDirectory,
        int columns,
        int rows)
    {
        var inputPipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        var outputPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);

        var inputHandle = new SafeFileHandle(inputPipe.ClientSafePipeHandle.DangerousGetHandle(), false);
        var outputHandle = new SafeFileHandle(outputPipe.ClientSafePipeHandle.DangerousGetHandle(), false);

        var size = new COORD { X = (short)columns, Y = (short)rows };
        int hr = CreatePseudoConsole(size, inputHandle, outputHandle, 0, out var hPc);
        if (hr != 0) throw new InvalidOperationException($"CreatePseudoConsole failed: 0x{hr:X}");

        var startupInfo = new STARTUPINFOEX();
        startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, out var size2);
        var attrListBuffer = Marshal.AllocHGlobal((int)size2);
        InitializeProcThreadAttributeList(attrListBuffer, 1, 0, out _);
        UpdateProcThreadAttribute(attrListBuffer, 0, (IntPtr)0x00020016, hPc.DangerousGetHandle(), (UIntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero);
        startupInfo.lpAttributeList = attrListBuffer;

        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
            }
        };
        process.Start();

        Marshal.FreeHGlobal(attrListBuffer);

        return new WindowsPtySession(hPc, inputPipe, outputPipe, process);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _process.Kill(); } catch { }
        _process.Dispose();
        _inputPipe.Dispose();
        _outputPipe.Dispose();
        ClosePseudoConsole(_hPseudoConsole);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD { public short X; public short Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO { public int cb; public string? lpReserved, lpDesktop, lpTitle; public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags; public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }

    [DllImport("kernel32.dll")]
    private static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint flags, out SafeFileHandle hPc);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(SafeFileHandle hPc);

    [DllImport("kernel32.dll")]
    private static extern int ResizePseudoConsole(SafeFileHandle hPc, COORD size);

    [DllImport("kernel32.dll")]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, out IntPtr lpSize);

    [DllImport("kernel32.dll")]
    private static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, UIntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);
}
