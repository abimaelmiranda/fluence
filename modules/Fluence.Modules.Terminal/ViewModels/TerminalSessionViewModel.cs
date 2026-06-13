using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Infrastructure;
using Fluence.Core.ViewModels;
using Fluence.Core.Workspace;
using XTerm.Events;
using XTerm.Options;
using XTerminal = global::XTerm.Terminal;

namespace Fluence.Modules.Terminal.ViewModels;

public sealed partial class TerminalSessionViewModel : ViewModelBase, IDisposable
{
    private readonly ITerminalSession _session;
    private readonly IWorkspaceContext _workspace;
    private readonly Action<TerminalSessionViewModel> _activate;
    private readonly CancellationTokenSource _bridgeCts = new();
    private readonly System.Text.Decoder _utf8Decoder = System.Text.Encoding.UTF8.GetDecoder();
    private readonly StringBuilder _pendingOutput = new();
    private readonly object _pendingOutputGate = new();
    private int _outputFlushScheduled;
    private int _bridgeStarted; // 0 = not started, 1 = started; use Interlocked
    private bool _hasInitialVisualResize;
    private volatile bool _disposed;
    private bool _isActive;
    private string _title = "Terminal";

    public TerminalSessionViewModel(
        ITerminalSession session,
        IWorkspaceContext workspace,
        Action<TerminalSessionViewModel> activate)
    {
        _session = session;
        _workspace = workspace;
        _activate = activate;

        XTerminal = new XTerminal(new TerminalOptions
        {
            Cols = 80,
            Rows = 24,
            Scrollback = 5000,
            TermName = "xterm-256color",
            ConvertEol = false,
        });

        _session.DataReceived += OnDataReceived;
        _session.Cleared += OnCleared;
    }

    public ITerminalSession Session => _session;

    public XTerminal XTerminal { get; }

    public event EventHandler? BufferRefreshed;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    [RelayCommand]
    private void Activate()
    {
        _activate(this);
    }

    public async Task StartShellAsync(string? workingDirectory = null, int? columns = null, int? rows = null)
    {
        if (_disposed || _session.IsClosing)
            return;

        var cols = columns ?? XTerminal.Cols;
        var rowCount = rows ?? XTerminal.Rows;

        await ResizeXTerminalAsync(cols, rowCount);
        _hasInitialVisualResize = true;

        await _session.StartShellAsync(workingDirectory, cols, rowCount);

        if (!_disposed && _session.HasStarted)
            await ResizeXTerminalAsync(cols, rowCount);

        if (!_disposed && _hasInitialVisualResize && _session.CanAcceptInput &&
            Interlocked.CompareExchange(ref _bridgeStarted, 1, 0) == 0)
        {
            _ = StartXtermBridgeAsync(_bridgeCts.Token);
        }
    }

    private async Task StartXtermBridgeAsync(CancellationToken cancellationToken)
    {
        // Delay before wiring DataReceived so zsh's readline (zle) has time to initialize.
        // Without the delay, XTerm.NET response bytes (e.g. ESC[?1;2c) arrive at PTY stdin
        // before zle is ready and get interpreted as user input, corrupting zsh's startup.
        if (!await DelayOrCancelledAsync(1000, cancellationToken))
            return;

        // Retry for up to 3 extra seconds if the session isn't ready yet. Without this,
        // a slow shell startup causes CanAcceptInput to be false here and the bridge is
        // silently skipped, leaving TUI apps unable to receive terminal query responses.
        for (var attempt = 0; attempt < 6; attempt++)
        {
            if (_disposed || cancellationToken.IsCancellationRequested) return;
            if (_session.CanAcceptInput) break;
            if (!await DelayOrCancelledAsync(500, cancellationToken))
                return;
        }

        if (_disposed || cancellationToken.IsCancellationRequested || !_session.CanAcceptInput)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && !cancellationToken.IsCancellationRequested && _session.CanAcceptInput)
            {
                try { XTerminal.DataReceived += OnXtermDataReceived; }
                catch (ObjectDisposedException) { }
            }
        });
    }

    private static async Task<bool> DelayOrCancelledAsync(int millisecondsDelay, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        if (!cancellationToken.CanBeCanceled)
        {
            await Task.Delay(millisecondsDelay);
            return true;
        }

        var cancellationCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            cancellationCompletion);
        var delayTask = Task.Delay(millisecondsDelay);
        return await Task.WhenAny(delayTask, cancellationCompletion.Task) == delayTask &&
            !cancellationToken.IsCancellationRequested;
    }

    private void OnXtermDataReceived(object? sender, TerminalEvents.DataEventArgs e)
    {
        if (!_disposed && _session.CanAcceptInput)
            _ = _session.SendInputAsync(e.Data);
    }

    public async Task SendInputAsync(string text)
    {
        if (!_disposed && _session.CanAcceptInput)
            await _session.SendInputAsync(text);
    }

    public async Task ResizeAsync(int cols, int rows)
    {
        if (_disposed || _session.IsClosing)
            return;

        await ResizeXTerminalAsync(cols, rows);
        _hasInitialVisualResize = true;

        await _session.ResizeAsync(cols, rows);
    }

    public async Task ExecuteAsync(string command)
    {
        await _session.ExecuteAsync(command, GetWorkingDirectory());
    }

    private void OnDataReceived(object? sender, TerminalDataEventArgs e)
    {
        // Use stateful decoder so multi-byte UTF-8 sequences split across PTY read
        // chunks are reconstructed correctly instead of producing replacement chars.
        int charCount = _utf8Decoder.GetCharCount(e.Data, 0, e.Data.Length, flush: false);
        if (charCount == 0) return;
        char[] chars = new char[charCount];
        _utf8Decoder.GetChars(e.Data, 0, e.Data.Length, chars, 0, flush: false);

        bool shouldSchedule;
        lock (_pendingOutputGate)
        {
            _pendingOutput.Append(chars, 0, charCount);
            shouldSchedule = _outputFlushScheduled++ == 0;
        }
        if (shouldSchedule)
            Dispatcher.UIThread.Post(FlushPendingOutput);
    }

    private void FlushPendingOutput()
    {
        string text;
        lock (_pendingOutputGate)
        {
            text = _pendingOutput.ToString();
            _pendingOutput.Clear();
            _outputFlushScheduled = 0;
        }
        if (_disposed || text.Length == 0) return;
        try
        {
            XTerminal.Write(text);
            BufferRefreshed?.Invoke(this, EventArgs.Empty);
        }
        catch (ObjectDisposedException) { }
    }

    private void OnCleared(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
            {
                try { XTerminal.Clear(); }
                catch (ObjectDisposedException) { }
            }
        });
    }

    public void WriteError(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed)
            {
                try { XTerminal.Write($"\x1b[31m{message}\x1b[0m"); }
                catch (ObjectDisposedException) { }
            }
        });
    }

    private async Task ResizeXTerminalAsync(int cols, int rows)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed)
                return;

            try { XTerminal.Resize(cols, rows); }
            catch (ObjectDisposedException) { }
        });
    }

    private string? GetWorkingDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_workspace.Current.CurrentFolderPath))
            return _workspace.Current.CurrentFolderPath;

        var solutionPath = _workspace.Current.CurrentSolutionPath;
        return string.IsNullOrWhiteSpace(solutionPath) ? null : Path.GetDirectoryName(solutionPath);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _bridgeCts.Cancel();
        _bridgeCts.Dispose();
        _session.DataReceived -= OnDataReceived;
        _session.Cleared -= OnCleared;
        try { XTerminal.DataReceived -= OnXtermDataReceived; }
        catch (ObjectDisposedException) { }
        lock (_pendingOutputGate)
        {
            _pendingOutput.Clear();
            _outputFlushScheduled = 0;
        }
        _utf8Decoder.Reset();
    }
}
