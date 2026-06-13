using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Fluence.Modules.Terminal.ViewModels;

namespace Fluence.Modules.Terminal.Views;

public partial class TerminalView : UserControl
{
    private TerminalViewModel? _viewModel;
    private TerminalSessionViewModel? _activeSession;
    private int _activeSessionVersion;
    private bool _activeSessionStartScheduled;
    private bool _isAttached;
    private CancellationTokenSource? _activeSessionStartCts;
    private CancellationTokenSource? _resizeDebounceCts;

    public TerminalView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as TerminalViewModel;
        if (_viewModel is null)
        {
            SetActiveSession(null);
            TerminalControl.Resized = null;
            return;
        }

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        TerminalControl.Resized = OnTerminalResized;
        SetActiveSession(_viewModel.ActiveSession);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalViewModel.ActiveSession))
            SetActiveSession(_viewModel?.ActiveSession);
    }

    private void SetActiveSession(TerminalSessionViewModel? session)
    {
        if (ReferenceEquals(_activeSession, session))
            return;

        if (_activeSession is not null)
            _activeSession.BufferRefreshed -= OnBufferRefreshed;

        CancelScheduledStart();
        CancelScheduledResize();
        _activeSession = session;
        _activeSessionVersion++;
        _activeSessionStartScheduled = false;
        TerminalControl.Terminal = session?.XTerminal;
        TerminalControl.TerminalTextInput = session is null
            ? null
            : text => session.SendInputAsync(text);

        if (_activeSession is not null)
            _activeSession.BufferRefreshed += OnBufferRefreshed;

        Dispatcher.UIThread.Post(() =>
        {
            TerminalControl.RequestRedraw();
            TerminalControl.RequestInitialResize();
            ScheduleStartForActiveSession();
        }, DispatcherPriority.Loaded);
    }

    private void OnBufferRefreshed(object? sender, EventArgs e)
    {
        TerminalControl.RequestRedraw();
    }

    private void OnTerminalResized(int cols, int rows)
    {
        if (!_isAttached || _activeSession is null)
            return;

        var session = _activeSession;
        var version = _activeSessionVersion;

        if (!session.Session.HasStarted)
        {
            ScheduleStartForActiveSession();
            return;
        }

        ScheduleResize(session, version, cols, rows);
    }

    private void ScheduleStartForActiveSession()
    {
        if (!_isAttached || _activeSession is null || _activeSessionStartScheduled || _activeSession.Session.HasStarted)
            return;

        _activeSessionStartScheduled = true;
        var session = _activeSession;
        var version = _activeSessionVersion;
        _activeSessionStartCts = new CancellationTokenSource();
        _ = StartActiveSessionWhenReadyAsync(session, version, _activeSessionStartCts.Token);
    }

    private async Task StartActiveSessionWhenReadyAsync(TerminalSessionViewModel session, int version, CancellationToken cancellationToken)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(TerminalControl.InvalidateArrange, DispatcherPriority.Loaded);
            await Task.Delay(16, cancellationToken); // one frame — enough for layout to complete

            if (!_isAttached ||
                !ReferenceEquals(_activeSession, session) ||
                version != _activeSessionVersion ||
                session.Session.IsClosing ||
                !ReferenceEquals(TerminalControl.Terminal, session.XTerminal))
                return;

            for (var attempt = 0; attempt < 80; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_isAttached ||
                    !ReferenceEquals(_activeSession, session) ||
                    version != _activeSessionVersion ||
                    session.Session.IsClosing ||
                    !ReferenceEquals(TerminalControl.Terminal, session.XTerminal))
                    return;

                var size = TerminalControl.Bounds.Size;
                if (size.Width > 0 && size.Height > 0)
                {
                    var (cols, rows) = TerminalControl.GetCurrentGridSize();

                    if (!_isAttached ||
                        !ReferenceEquals(_activeSession, session) ||
                        version != _activeSessionVersion ||
                        session.Session.IsClosing ||
                        !ReferenceEquals(TerminalControl.Terminal, session.XTerminal))
                        return;

                    await session.StartShellAsync(columns: cols, rows: rows);
                    if (ReferenceEquals(_activeSession, session) &&
                        version == _activeSessionVersion)
                    {
                        _activeSessionStartScheduled = false;
                        TerminalControl.RequestRedraw();
                        ScheduleResize(session, version, cols, rows);
                    }
                    return;
                }

                await Task.Delay(25, cancellationToken);
            }

            if (ReferenceEquals(_activeSession, session) &&
                version == _activeSessionVersion &&
                !session.Session.IsClosing &&
                !session.Session.HasStarted)
            {
                _activeSessionStartScheduled = false;
                TerminalControl.InvalidateArrange();
                return;
            }

            if (ReferenceEquals(_activeSession, session) &&
                version == _activeSessionVersion &&
                !session.Session.IsClosing)
            {
                session.WriteError("\r\n[Terminal error: terminal surface did not become ready]\r\n");
            }
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_activeSession, session) && version == _activeSessionVersion)
            {
                _activeSessionStartScheduled = false;
                // Startup was cancelled (e.g. brief detach/reattach). If this session is
                // still active and hasn't started, reschedule — otherwise it stays stuck.
                if (_isAttached && !session.Session.HasStarted && !session.Session.IsClosing)
                    ScheduleStartForActiveSession();
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_activeSession, session) && version == _activeSessionVersion)
                _activeSessionStartScheduled = false;

            if (!session.Session.IsClosing)
                session.WriteError($"\r\n[Terminal error: {ex.Message}]\r\n");
        }
    }

    private async Task ResizeActiveSessionAsync(TerminalSessionViewModel session, int version, int cols, int rows)
    {
        try
        {
            if (!ReferenceEquals(_activeSession, session) ||
                version != _activeSessionVersion ||
                session.Session.IsClosing ||
                !ReferenceEquals(TerminalControl.Terminal, session.XTerminal))
                return;

            await session.ResizeAsync(cols, rows);
        }
        catch
        {
            // Ignore stale resize callbacks from sessions that are closing or no longer active.
        }
    }

    private void ScheduleResize(TerminalSessionViewModel session, int version, int cols, int rows)
    {
        var old = Interlocked.Exchange(ref _resizeDebounceCts, new CancellationTokenSource());
        if (old is not null)
        {
            try { old.Cancel(); } catch (ObjectDisposedException) { }
            old.Dispose();
        }
        var token = _resizeDebounceCts!.Token;
        _ = ResizeAfterDelayAsync(session, version, cols, rows, token);
    }

    private async Task ResizeAfterDelayAsync(TerminalSessionViewModel session, int version, int cols, int rows, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(75, cancellationToken);
            await ResizeActiveSessionAsync(session, version, cols, rows);
            if (ReferenceEquals(_activeSession, session) && version == _activeSessionVersion)
                TerminalControl.RequestRedraw();
        }
        catch (OperationCanceledException) { }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        Dispatcher.UIThread.Post(() =>
        {
            TerminalControl.RequestInitialResize();
            ScheduleStartForActiveSession();
        }, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        CancelScheduledStart();
        CancelScheduledResize();
        base.OnDetachedFromVisualTree(e);
    }

    private void CancelScheduledStart()
    {
        var cts = Interlocked.Exchange(ref _activeSessionStartCts, null);
        if (cts is null) return;
        try { cts.Cancel(); } catch (ObjectDisposedException) { }
        cts.Dispose();
    }

    private void CancelScheduledResize()
    {
        var cts = Interlocked.Exchange(ref _resizeDebounceCts, null);
        if (cts is null) return;
        try { cts.Cancel(); } catch (ObjectDisposedException) { }
        cts.Dispose();
    }
}
