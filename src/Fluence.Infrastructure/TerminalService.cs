using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Infrastructure;

namespace Fluence.Infrastructure;

public sealed class TerminalService : ITerminalService, IAsyncDisposable
{
    private const int TerminalLimit = 2;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5);

    private readonly IPtyHost _ptyHost;
    private readonly object _sessionsGate = new();
    private readonly List<TerminalSession> _sessions = [];
    private bool _disposed;

    public TerminalService(IPtyHost ptyHost)
    {
        _ptyHost = ptyHost;
    }

    public event EventHandler? SessionsChanged;

    public int MaxSessions => TerminalLimit;

    public bool IsBusy => HasActiveSession;

    public bool HasActiveSession => ActiveSession is { HasExited: false };

    public bool CanCreateSession
    {
        get
        {
            lock (_sessionsGate)
                return _sessions.Count < MaxSessions;
        }
    }

    public IReadOnlyList<ITerminalSession> Sessions
    {
        get
        {
            lock (_sessionsGate)
                return _sessions.ToArray();
        }
    }

    public ITerminalSession? ActiveSession { get; private set; }

    public ITerminalSession CreateSession()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var session = new TerminalSession(_ptyHost);
        session.TerminalEnded += OnSessionTerminalEnded;

        lock (_sessionsGate)
        {
            if (_sessions.Count >= MaxSessions)
                throw new InvalidOperationException($"A maximum of {MaxSessions} terminal sessions is allowed.");

            _sessions.Add(session);
            ActiveSession = session;
        }

        SessionsChanged?.Invoke(this, EventArgs.Empty);
        return session;
    }

    public Task CloseSessionAsync(ITerminalSession session)
    {
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (session is not TerminalSession terminalSession)
                return Task.CompletedTask;

            lock (_sessionsGate)
            {
                var index = _sessions.IndexOf(terminalSession);
                if (index < 0)
                    return Task.CompletedTask;

                terminalSession.TerminalEnded -= OnSessionTerminalEnded;
                _sessions.RemoveAt(index);
                if (ReferenceEquals(ActiveSession, session))
                {
                    ActiveSession = _sessions.Count == 0
                        ? null
                        : _sessions[Math.Clamp(index - 1, 0, _sessions.Count - 1)];
                }
            }

            terminalSession.BeginClose();
            _ = DisposeSessionAsync(terminalSession);
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // Closing a terminal must never take down the shell.
        }

        return Task.CompletedTask;
    }

    public void SetActiveSession(ITerminalSession session)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_sessionsGate)
        {
            if (session is not TerminalSession terminalSession || !_sessions.Contains(terminalSession))
                return;

            if (ReferenceEquals(ActiveSession, session))
                return;

            ActiveSession = terminalSession;
        }

        SessionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSessionTerminalEnded(object? sender, EventArgs e)
    {
        if (sender is TerminalSession session)
            _ = CloseSessionAsync(session);
    }

    public async Task ExecuteAsync(
        string command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        command = command.Trim();
        if (string.IsNullOrWhiteSpace(command))
            return;

        var session = ActiveSession;
        if (session is null || session.HasExited)
            session = CreateSession();

        await session.ExecuteAsync(command, workingDirectory, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        TerminalSession[] sessions;
        lock (_sessionsGate)
        {
            sessions = _sessions.ToArray();
            _sessions.Clear();
            ActiveSession = null;
        }

        foreach (var session in sessions)
        {
            session.TerminalEnded -= OnSessionTerminalEnded;
            session.BeginClose();
        }

        await Task.WhenAll(sessions.Select(session => session.DisposeAsync().AsTask())).ConfigureAwait(false);
    }

    private static async Task DisposeSessionAsync(TerminalSession session)
    {
        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Session teardown is best-effort; the UI state has already moved on.
        }
    }

    private sealed class TerminalSession : ITerminalSession
    {
        private readonly IPtyHost _ptyHost;
        private readonly object _stateGate = new();
        private readonly object _pendingGate = new();
        private readonly Queue<PendingCommand> _pendingCommands = [];
        private readonly SemaphoreSlim _startGate = new(1, 1);
        private readonly SemaphoreSlim _writeGate = new(1, 1);
        private readonly CancellationTokenSource _closeCts = new();
        private int _disposeStarted;
        private IPtySession? _ptySession;
        private CancellationTokenSource? _readCts;
        private string? _shellWorkingDirectory;
        private TerminalSessionState _state = TerminalSessionState.Created;
        private TerminalProcessState _processState = TerminalProcessState.Uninitialized;

        public TerminalSession(IPtyHost ptyHost)
        {
            _ptyHost = ptyHost;
        }

        public event EventHandler<TerminalDataEventArgs>? DataReceived;
        public event EventHandler? Cleared;
        public event EventHandler? TerminalEnded;

        public bool HasStarted => State == TerminalSessionState.Started;
        public bool HasExited => State is TerminalSessionState.Closed or TerminalSessionState.Abandoned or TerminalSessionState.FailedStartup || IsTerminalProcessKilled(ProcessState) || _ptySession is { HasExited: true };
        public bool IsClosing => IsTerminalInactive(State);
        public bool CanAcceptInput => State == TerminalSessionState.Started && ProcessState == TerminalProcessState.Running && _ptySession is { HasExited: false };

        private TerminalSessionState State
        {
            get
            {
                lock (_stateGate)
                    return _state;
            }
            set
            {
                lock (_stateGate)
                    _state = value;
            }
        }

        private TerminalProcessState ProcessState
        {
            get
            {
                lock (_stateGate)
                    return _processState;
            }
            set
            {
                lock (_stateGate)
                    _processState = value;
            }
        }

        public void BeginClose()
        {
            MarkClosing();
            TryCancelReadLoop();
            ClearPendingCommands();
        }

        public async Task StartShellAsync(
            string? workingDirectory = null,
            int columns = 80,
            int rows = 24,
            CancellationToken cancellationToken = default)
        {
            if (IsTerminalInactive(State))
                return;

            await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsTerminalInactive(State))
                    return;

                if (_ptySession is { HasExited: false })
                {
                    State = TerminalSessionState.Started;
                    await DrainPendingCommandsAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                lock (_stateGate)
                {
                    _state = TerminalSessionState.Starting;
                    _processState = TerminalProcessState.Launching;
                }
                var dir = ResolveDirectory(workingDirectory);
                _shellWorkingDirectory = dir;

                _readCts?.Cancel();
                _readCts?.Dispose();
                _readCts = new CancellationTokenSource();
                var ct = _readCts.Token;

                var ptySession = await CreatePtySessionAsync(
                    dir,
                    columns,
                    rows,
                    cancellationToken).ConfigureAwait(false);

                ptySession.Exited += OnSessionExited;

                var disposeCreatedPty = false;
                lock (_stateGate)
                {
                    if (IsTerminalInactive(_state))
                    {
                        disposeCreatedPty = true;
                    }
                    else
                    {
                        _ptySession = ptySession;
                        _state = TerminalSessionState.Started;
                        _processState = TerminalProcessState.Running;
                    }
                }

                if (disposeCreatedPty)
                {
                    ptySession.Exited -= OnSessionExited;
                    _ = DisposePtySessionAsync(ptySession);
                    return;
                }

                StartReadLoop(ptySession, ct);

                if (State == TerminalSessionState.Started)
                    await DrainPendingCommandsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                if (IsTerminalInactive(State))
                    return;

                if (State == TerminalSessionState.Starting)
                {
                    MarkStartupFailed(TerminalProcessState.KilledDuringLaunch);
                    return;
                }

                throw;
            }
            finally
            {
                _startGate.Release();
            }
        }

        public async Task SendInputAsync(string text, CancellationToken cancellationToken = default)
        {
            if (!CanAcceptInput)
                return;

            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!CanAcceptInput || _ptySession is null)
                    return;

                var bytes = Encoding.UTF8.GetBytes(text);
                await Task.Run(() =>
                {
                    try
                    {
                        _ptySession.Input.Write(bytes, 0, bytes.Length);
                        _ptySession.Input.Flush();
                    }
                    catch (IOException) { }
                    catch (ObjectDisposedException) { }
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public async Task ResizeAsync(int columns, int rows)
        {
            if (State != TerminalSessionState.Started)
                return;

            await _writeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (State == TerminalSessionState.Started)
                    _ptySession?.Resize(columns, rows);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public async Task ExecuteAsync(
            string command,
            string? workingDirectory = null,
            CancellationToken cancellationToken = default)
        {
            if (IsTerminalInactive(State))
                return;

            command = command.Trim();
            if (string.IsNullOrWhiteSpace(command))
                return;

            if (!CanAcceptInput)
            {
                lock (_pendingGate)
                    _pendingCommands.Enqueue(new PendingCommand(command, workingDirectory));
                return;
            }

            await SendCommandAsync(command, workingDirectory, cancellationToken).ConfigureAwait(false);
        }

        public Task CancelAsync()
        {
            return SendInputAsync("\x03");
        }

        public void Clear()
        {
            if (!IsTerminalInactive(State))
                Cleared?.Invoke(this, EventArgs.Empty);
        }

        private async Task DrainPendingCommandsAsync(CancellationToken cancellationToken)
        {
            while (State == TerminalSessionState.Started)
            {
                PendingCommand pendingCommand;
                lock (_pendingGate)
                {
                    if (!_pendingCommands.TryDequeue(out pendingCommand))
                        return;
                }

                await SendCommandAsync(
                    pendingCommand.Command,
                    pendingCommand.WorkingDirectory,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task SendCommandAsync(
            string command,
            string? workingDirectory,
            CancellationToken cancellationToken)
        {
            if (!CanAcceptInput)
                return;

            string toSend;
            if (!string.IsNullOrWhiteSpace(workingDirectory) &&
                !string.Equals(workingDirectory, _shellWorkingDirectory, StringComparison.OrdinalIgnoreCase))
            {
                toSend = $"cd {QuoteShellPath(workingDirectory)} && {command}\r";
            }
            else
            {
                toSend = command + "\r";
            }

            await SendInputAsync(toSend, cancellationToken).ConfigureAwait(false);
        }

        private void StartReadLoop(IPtySession session, CancellationToken ct)
        {
            var t = new Thread(() => ReadThreadProc(session, ct))
            {
                IsBackground = true,
                Name = "pty-read-loop",
            };
            t.Start();
        }

        private void ReadThreadProc(IPtySession session, CancellationToken ct)
        {
            var buf = new byte[4096];
            try
            {
                while (!ct.IsCancellationRequested && !session.HasExited && ProcessState == TerminalProcessState.Running)
                {
                    int n;
                    try { n = session.Output.Read(buf, 0, buf.Length); }
                    catch (IOException) { break; }
                    catch (ObjectDisposedException) { break; }
                    catch (InvalidOperationException) { break; }

                    if (n <= 0) break;

                    var chunk = new byte[n];
                    Buffer.BlockCopy(buf, 0, chunk, 0, n);
                    if (IsTerminalInactive(State) || ProcessState != TerminalProcessState.Running)
                        break;

                    DataReceived?.Invoke(this, new TerminalDataEventArgs(chunk));
                }
            }
            catch (ObjectDisposedException) { }
            catch when (ct.IsCancellationRequested) { }
        }

        private void OnSessionExited(object? sender, EventArgs e)
        {
            TryCancelReadLoop();
            var shouldNotifyEnded = false;

            lock (_stateGate)
            {
                if (IsTerminalInactive(_state))
                    return;

                _processState = _processState == TerminalProcessState.KilledByUser
                    ? TerminalProcessState.KilledByUser
                    : TerminalProcessState.KilledByProcess;
                _state = TerminalSessionState.Closed;
                shouldNotifyEnded = true;
            }

            if (shouldNotifyEnded)
                TerminalEnded?.Invoke(this, EventArgs.Empty);
        }

        private static string ResolveDirectory(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                return path;
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private static string QuoteShellPath(string path)
        {
            // Single-quote the path and escape embedded single-quotes for POSIX shells.
            return "'" + path.Replace("'", "'\\''") + "'";
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) == 1)
                return ValueTask.CompletedTask;

            MarkClosing();
            TryCancelReadLoop();

            var ptySession = DetachPtySession();
            ClearPendingCommands();

            try
            {
                _readCts?.Dispose();
                _readCts = null;

                if (ptySession is not null)
                    _ = GracefulDisposeAsync(ptySession);

                State = TerminalSessionState.Closed;
            }
            catch
            {
                State = TerminalSessionState.Closed;
            }

            return ValueTask.CompletedTask;
        }

        private bool MarkClosing()
        {
            lock (_stateGate)
            {
                if (_state == TerminalSessionState.Closed)
                    return false;

                _state = _state is TerminalSessionState.Created or TerminalSessionState.Starting or TerminalSessionState.Abandoned or TerminalSessionState.FailedStartup
                    ? TerminalSessionState.Abandoned
                    : TerminalSessionState.Closing;
                _processState = TerminalProcessState.KilledByUser;
                try { _closeCts.Cancel(); }
                catch (ObjectDisposedException) { }
                return true;
            }
        }

        private void MarkStartupFailed(TerminalProcessState processState)
        {
            lock (_stateGate)
            {
                if (IsTerminalInactive(_state))
                    return;

                _state = TerminalSessionState.FailedStartup;
                _processState = processState;
                try { _closeCts.Cancel(); }
                catch (ObjectDisposedException) { }
            }

            TryCancelReadLoop();
            ClearPendingCommands();
            TerminalEnded?.Invoke(this, EventArgs.Empty);
        }

        private async Task<IPtySession> CreatePtySessionAsync(
            string workingDirectory,
            int columns,
            int rows,
            CancellationToken cancellationToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closeCts.Token);
            var createTask = Task.Run(() => _ptyHost.CreateShellSession(workingDirectory, columns, rows));
            var lateDisposeScheduled = false;

            try
            {
                var delayTask = Task.Delay(StartupTimeout, linkedCts.Token);
                var completedTask = await Task.WhenAny(createTask, delayTask).ConfigureAwait(false);
                if (completedTask == createTask)
                    return await createTask.ConfigureAwait(false);

                if (linkedCts.IsCancellationRequested)
                    throw new OperationCanceledException(linkedCts.Token);

                _ = DisposeLatePtySessionAsync(createTask);
                lateDisposeScheduled = true;
                throw new TimeoutException("terminal shell startup timed out");
            }
            catch
            {
                if (!lateDisposeScheduled && !createTask.IsCompleted)
                    _ = DisposeLatePtySessionAsync(createTask);
                throw;
            }
        }

        private static async Task DisposeLatePtySessionAsync(Task<IPtySession> createTask)
        {
            try
            {
                var ptySession = await createTask.ConfigureAwait(false);
                await DisposePtySessionAsync(ptySession).ConfigureAwait(false);
            }
            catch
            {
                // Late startup completion after timeout/close is best-effort only.
            }
        }

        private IPtySession? DetachPtySession()
        {
            var ptySession = Interlocked.Exchange(ref _ptySession, null);
            if (ptySession is not null)
            {
                try { ptySession.Exited -= OnSessionExited; }
                catch { }
            }

            return ptySession;
        }

        private const int GracefulShutdownMs = 250;

        private static async Task GracefulDisposeAsync(IPtySession ptySession)
        {
            // Brief grace period — allows PTY to flush trailing data before kill.
            // VSCode uses 250ms (ShutdownConstants.DataFlushTimeout in terminalProcess.ts).
            await Task.Delay(GracefulShutdownMs).ConfigureAwait(false);
            await DisposePtySessionAsync(ptySession).ConfigureAwait(false);
        }

        private static Task DisposePtySessionAsync(IPtySession ptySession)
        {
            return Task.Run(() =>
            {
                try { ptySession.Dispose(); }
                catch { }
            });
        }

        private void TryCancelReadLoop()
        {
            try { _readCts?.Cancel(); }
            catch (ObjectDisposedException) { }
        }

        private void ClearPendingCommands()
        {
            lock (_pendingGate)
                _pendingCommands.Clear();
        }

        private static bool IsTerminalInactive(TerminalSessionState state)
        {
            return state is TerminalSessionState.Closing or TerminalSessionState.Closed or TerminalSessionState.Abandoned or TerminalSessionState.FailedStartup;
        }

        private static bool IsTerminalProcessKilled(TerminalProcessState state)
        {
            return state is TerminalProcessState.KilledDuringLaunch or TerminalProcessState.KilledByUser or TerminalProcessState.KilledByProcess;
        }

        private sealed record PendingCommand(string Command, string? WorkingDirectory);

        private enum TerminalSessionState
        {
            Created,
            Starting,
            Started,
            FailedStartup,
            Abandoned,
            Closing,
            Closed,
        }

        private enum TerminalProcessState
        {
            Uninitialized,
            Launching,
            Running,
            KilledDuringLaunch,
            KilledByUser,
            KilledByProcess,
        }
    }
}
