using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Jobs;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Abstractions.Toolchains;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Events.Debug;
using Fluence.Core.Events.Ui;
using Fluence.Core.Events.Workspace;
using IoFile = System.IO.File;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Jobs;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Models.Output;
using Fluence.Core.Models.Toolchains;
using Fluence.Core.Models.Workbench;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Core.Services.Toolchains;

public sealed class ToolchainDebugService(
    IToolchainRegistry registry,
    IDebugStateService debugState,
    IWorkspaceContext workspace,
    IOutputChannelService output,
    IUserNotificationService notifications,
    IShellRegionHost shellRegions,
    IShellEventBus events,
    IExclusiveJobCoordinator jobs,
    ITaskScheduler scheduler,
    IUiDispatcher dispatcher,
    ILocalizationService localization) : IDebugService, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IDebugAdapterClient? _adapter;
    private IExclusiveJobLease? _jobLease;
    private bool _adapterStarted;
    private int _sessionGeneration;
    private string? _workspaceRoot;
    private readonly ILocalizationService _loc = localization;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (registry.Active is not IDebugToolchain toolchain)
        {
            notifications.ShowWarning("Debug", $"Toolchain '{registry.Active.Id}' does not support debugging.");
            return;
        }

        if (!await toolchain.EnsureAsync(ToolchainCapability.Debugger, cancellationToken).ConfigureAwait(false))
            return;

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) { return; }
        try
        {
            if (_adapter is not null)
            {
                ShowWarning(_loc.Get("Debug.Notification.SessionAlreadyRunning"));
                return;
            }

            if (!jobs.TryAcquire(ExclusiveJobKind.Debug, out var lease))
            {
                ShowWarning(FormatJobBlockedMessage(jobs.ActiveJob, _loc.Get("Debug.Job.DebugSession")));
                return;
            }

            _jobLease = lease;

            DebugAdapterSession? session;
            try
            {
                session = await toolchain.PrepareDebugSessionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (FileNotFoundException ex)
            {
                ShowWarning(ex.Message);
                ReleaseDebugJobLease();
                return;
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                ReleaseDebugJobLease();
                return;
            }

            if (session is null)
            {
                ReleaseDebugJobLease();
                return;
            }

            Interlocked.Increment(ref _sessionGeneration);
            _workspaceRoot = session.LaunchRequest.WorkspaceRoot;
            _adapter = session.Adapter;
            _adapter.Stopped += OnAdapterStopped;
            _adapter.Terminated += OnAdapterTerminated;
            _adapter.Continued += OnAdapterContinued;
            _adapter.OutputReceived += OnAdapterOutputReceived;

            debugState.StartSession(session.LaunchRequest.Configuration?.Architecture ?? string.Empty);
            workspace.SetMode(WorkspaceMode.Debugging);
            events.Publish(new ActivityBarTabSelectRequestedEvent("Debug"));
            SelectSidebar();
            SelectBottomBar(BottomBarTabIds.Debug);

            await output.WriteAsync(
                OutputChannelIds.Debug,
                $"[debug] program: {session.LaunchRequest.ProgramPath}\r\n[debug] cwd: {session.LaunchRequest.WorkingDirectory}\r\n",
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await _adapter.StartAsync(session.LaunchRequest, cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(
                SyncBreakpointsAsync(debugState.Snapshot.Breakpoints, cancellationToken),
                _adapter.SetExceptionBreakpointsAsync(session.ExceptionBreakMode, cancellationToken)).ConfigureAwait(false);
            await _adapter.CompleteConfigurationAsync(cancellationToken).ConfigureAwait(false);
            _adapterStarted = true;
            debugState.Continue();
            await output.WriteAsync(OutputChannelIds.Debug, "[debug] Session started\r\n").ConfigureAwait(false);
        }
        catch (FileNotFoundException ex)
        {
            ShowWarning(ex.Message);
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) { return; }
        try
        {
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) { return; }
        try
        {
            if (_adapter is null)
            {
                ShowWarning(_loc.Get("Debug.Notification.NoActiveSessionToReload"));
                return;
            }

            await StopCoreAsync(cancellationToken, releaseJob: false).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        await StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task ContinueAsync(CancellationToken cancellationToken = default) =>
        RunAdapterCommandAsync(adapter => adapter.ContinueAsync(cancellationToken));

    public Task StepOverAsync(CancellationToken cancellationToken = default) =>
        RunAdapterCommandAsync(adapter => adapter.StepOverAsync(cancellationToken));

    public Task StepIntoAsync(CancellationToken cancellationToken = default) =>
        RunAdapterCommandAsync(adapter => adapter.StepIntoAsync(cancellationToken));

    public Task StepOutAsync(CancellationToken cancellationToken = default) =>
        RunAdapterCommandAsync(adapter => adapter.StepOutAsync(cancellationToken));

    public async Task<IReadOnlyList<DebugVariable>> GetChildVariablesAsync(int variablesReference, CancellationToken cancellationToken = default)
    {
        var adapter = _adapter;
        if (adapter is null)
            return [];

        try
        {
            return await adapter.GetChildVariablesAsync(variablesReference, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return [];
        }
    }

    public async Task<DebugVariable?> EvaluateAsync(string expression, CancellationToken cancellationToken = default)
    {
        var adapter = _adapter;
        if (adapter is null)
            return null;

        var snapshot = debugState.Snapshot;
        if (!snapshot.IsStopped)
            return null;

        var frameId = snapshot.StackFrames.FirstOrDefault()?.Id ?? 0;

        try
        {
            return await adapter.EvaluateAsync(expression, frameId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task ToggleBreakpointAsync(string filePath, int line, CancellationToken cancellationToken = default)
    {
        var breakpoints = debugState.ToggleBreakpoint(filePath, line);
        if (_adapter is not null)
        {
            var fileBreakpoints = breakpoints
                .Where(b => string.Equals(b.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var verified = await _adapter.SetBreakpointsForFileAsync(filePath, fileBreakpoints, cancellationToken).ConfigureAwait(false);
            debugState.SetBreakpointVerification(filePath, verified.ToDictionary(b => b.Line, b => b));
        }
    }

    private async Task RunAdapterCommandAsync(Func<IDebugAdapterClient, Task> command)
    {
        var adapter = _adapter;
        if (adapter is null)
            return;

        var snapshot = debugState.Snapshot;
        if (!snapshot.IsStopped || snapshot.ActiveThreadId is null)
        {
            await output.WriteAsync(OutputChannelIds.Debug, "[debug] Process is running; step/continue is available after a stop event.\r\n").ConfigureAwait(false);
            return;
        }

        debugState.Continue();
        try
        {
            await command(adapter).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await output.WriteAsync(OutputChannelIds.Debug, "[debug] Debug adapter did not respond to command in time.\r\n", OutputLogLevel.Error).ConfigureAwait(false);
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken, bool releaseJob = true)
    {
        Interlocked.Increment(ref _sessionGeneration);
        _adapterStarted = false;
        if (_adapter is not null)
        {
            _adapter.Stopped -= OnAdapterStopped;
            _adapter.Terminated -= OnAdapterTerminated;
            _adapter.Continued -= OnAdapterContinued;
            _adapter.OutputReceived -= OnAdapterOutputReceived;
            await _adapter.StopAsync(cancellationToken).ConfigureAwait(false);
            await _adapter.DisposeAsync().ConfigureAwait(false);
            _adapter = null;

            _workspaceRoot = null;
            debugState.EndSession();
            var previousMode = workspace.Current.ModeBeforeDebugging;
            if (workspace.Current.Mode == WorkspaceMode.Debugging && previousMode is not null)
                workspace.SetMode(previousMode.Value);
            shellRegions.Refresh();
        }

        if (releaseJob)
            ReleaseDebugJobLease();
    }

    private void OnAdapterStopped(object? sender, DebugAdapterStoppedEvent e)
    {
        var sessionGeneration = Volatile.Read(ref _sessionGeneration);
        scheduler.Schedule(
            "debug.inspect",
            TaskPriority.Interactive,
            ct => RefreshInspectionAsync(e, sessionGeneration, ct),
            correlationId: sessionGeneration);
    }

    private async Task RefreshInspectionAsync(DebugAdapterStoppedEvent e, int sessionGeneration, CancellationToken cancellationToken)
    {
        try
        {
            var adapter = _adapter;
            if (adapter is null || sessionGeneration != Volatile.Read(ref _sessionGeneration))
                return;

            var frames = await adapter.GetStackTraceAsync(e.ThreadId, cancellationToken).ConfigureAwait(false);
            if (sessionGeneration != Volatile.Read(ref _sessionGeneration))
                return;

            var workspaceRoot = _workspaceRoot;
            var currentFrame = SelectStoppedFrame(frames, workspaceRoot);
            var variables = currentFrame is null
                ? Array.Empty<DebugVariable>()
                : await adapter.GetVariablesAsync(currentFrame.Id, cancellationToken).ConfigureAwait(false);
            if (sessionGeneration != Volatile.Read(ref _sessionGeneration))
                return;

            var currentLine = !string.IsNullOrWhiteSpace(currentFrame?.FilePath) && currentFrame.Line > 0
                ? new DebugExecutionLine(currentFrame.FilePath!, currentFrame.Line)
                : null;
            if (sessionGeneration != Volatile.Read(ref _sessionGeneration))
                return;

            if (currentLine is not null)
                OpenStoppedFile(currentLine.FilePath);

            var exceptionInfo = IsExceptionStop(e) ? CreateExceptionInfo(e) : null;
            if (sessionGeneration != Volatile.Read(ref _sessionGeneration))
                return;

            debugState.SetStopped(e.Reason, e.ThreadId, currentLine, exceptionInfo);
            debugState.SetInspectionData(frames, variables);
            await output.WriteAsync(
                OutputChannelIds.Debug,
                FormatStoppedOutput(e, exceptionInfo),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (exceptionInfo is not null && currentLine is null)
                ShowError(exceptionInfo.Message);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await output.WriteAsync(OutputChannelIds.Debug, $"[debug] Error refreshing inspection: {ex.Message}\r\n", OutputLogLevel.Error)
                .ConfigureAwait(false);
        }
    }

    private void OnAdapterTerminated(object? sender, DebugAdapterTerminatedEvent e)
    {
        if (!_adapterStarted)
        {
            scheduler.Schedule(
                "debug.output",
                TaskPriority.Background,
                ct => output.WriteAsync(
                    OutputChannelIds.Debug,
                    _loc.Get("Debug.Log.NetcoredbgTerminated") + "\r\n",
                    OutputLogLevel.Error,
                    ct));
        }
        scheduler.Schedule("debug.stop", TaskPriority.Critical, StopAsync);
    }

    private void OnAdapterContinued(object? sender, DebugAdapterContinuedEvent e) =>
        debugState.Continue();

    private void OnAdapterOutputReceived(object? sender, DebugAdapterOutputEvent e) =>
        ScheduleDebugOutput(e.Text, e.IsError);

    private void ScheduleDebugOutput(string text, bool isError)
    {
        scheduler.Schedule(
            "debug.output",
            isError ? TaskPriority.Interactive : TaskPriority.Background,
            ct => output.WriteAsync(
                OutputChannelIds.Debug,
                text,
                isError ? OutputLogLevel.Error : OutputLogLevel.Information,
                ct));
    }

    private async Task SyncBreakpointsAsync(IReadOnlyList<DebugBreakpoint> breakpoints, CancellationToken cancellationToken)
    {
        var adapter = _adapter;
        if (adapter is null)
            return;

        var verificationByFile = await adapter.SetBreakpointsAsync(breakpoints, cancellationToken).ConfigureAwait(false);
        foreach (var file in verificationByFile)
        {
            debugState.SetBreakpointVerification(file.Key, file.Value.ToDictionary(b => b.Line, b => b));

            foreach (var breakpoint in file.Value.Where(b => !b.IsVerified))
            {
                await output.WriteAsync(
                    OutputChannelIds.Debug,
                    $"[debug] Breakpoint pending: {Path.GetFileName(file.Key)}:{breakpoint.Line}\r\n")
                    .ConfigureAwait(false);
            }
        }
    }

    private void OpenStoppedFile(string filePath)
    {
        if (!IoFile.Exists(filePath))
            return;

        if (dispatcher.CheckAccess())
        {
            events.Publish(new OpenFileRequestedEvent(filePath));
            return;
        }

        dispatcher.Post(() => events.Publish(new OpenFileRequestedEvent(filePath)));
    }

    private void SelectBottomBar(string tabId)
    {
        events.Publish(new SelectBottomBarTabEvent(tabId));

        if (dispatcher.CheckAccess())
        {
            shellRegions.Expand(ShellRegion.BottomBar);
            return;
        }

        dispatcher.Post(() => shellRegions.Expand(ShellRegion.BottomBar));
    }

    private void SelectSidebar()
    {
        if (dispatcher.CheckAccess())
        {
            shellRegions.Expand(ShellRegion.Sidebar);
            return;
        }

        dispatcher.Post(() => shellRegions.Expand(ShellRegion.Sidebar));
    }

    private static DebugStackFrame? SelectStoppedFrame(IReadOnlyList<DebugStackFrame> frames, string? workspaceRoot) =>
        frames.FirstOrDefault(frame => IsWorkspaceSourceFrame(frame, workspaceRoot)) ??
        frames.FirstOrDefault(IsSourceFrame) ??
        frames.FirstOrDefault();

    private static bool IsWorkspaceSourceFrame(DebugStackFrame frame, string? workspaceRoot)
    {
        if (!IsSourceFrame(frame) || string.IsNullOrWhiteSpace(workspaceRoot))
            return false;

        var fullPath = Path.GetFullPath(frame.FilePath!);
        var fullRoot = Path.GetFullPath(workspaceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSourceFrame(DebugStackFrame frame) =>
        frame.Line > 0 &&
        !string.IsNullOrWhiteSpace(frame.FilePath) &&
        IoFile.Exists(frame.FilePath);

    private static bool IsExceptionStop(DebugAdapterStoppedEvent e) =>
        string.Equals(e.Reason, "exception", StringComparison.OrdinalIgnoreCase);

    private DebugExceptionInfo CreateExceptionInfo(DebugAdapterStoppedEvent e)
    {
        var title = FirstNonEmpty(e.Text, e.Description, _loc.Get("Debug.Exception.Title"));
        var message = FirstNonEmpty(e.Description, e.Text, _loc.Get("Debug.Exception.Message"));

        if (string.Equals(title, message, StringComparison.Ordinal))
            message = _loc.Get("Debug.Exception.Message");

        return new DebugExceptionInfo(TrimForPopup(title, 120), TrimForPopup(message, 220));
    }

    private string FormatStoppedOutput(DebugAdapterStoppedEvent e, DebugExceptionInfo? exceptionInfo)
    {
        if (exceptionInfo is null)
            return $"[debug] Stopped: {e.Reason ?? _loc.Get("Debug.Session.Breakpoint")}\r\n";

        return string.Format(_loc.Get("Debug.Log.Exception"), exceptionInfo.Title, exceptionInfo.Message) + "\r\n";
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string TrimForPopup(string value, int maxLength)
    {
        value = value.Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Trim();

        return value.Length <= maxLength
            ? value
            : value[..Math.Max(0, maxLength - 3)] + "...";
    }

    private void ShowWarning(string message)
    {
        if (dispatcher.CheckAccess())
        {
            notifications.ShowWarning(_loc.Get("Debug.Title"), message);
            return;
        }

        dispatcher.Post(() => notifications.ShowWarning(_loc.Get("Debug.Title"), message));
    }

    private void ShowError(string message)
    {
        if (dispatcher.CheckAccess())
        {
            notifications.ShowError(_loc.Get("Debug.Title"), message);
            return;
        }

        dispatcher.Post(() => notifications.ShowError(_loc.Get("Debug.Title"), message));
    }

    private void ReleaseDebugJobLease()
    {
        _jobLease?.Dispose();
        _jobLease = null;
    }

    private string FormatJobBlockedMessage(ExclusiveJobKind? activeJob, string requestedJob)
    {
        var activeName = activeJob switch
        {
            ExclusiveJobKind.Debug => _loc.Get("Debug.Job.DebugSession"),
            ExclusiveJobKind.Run => _loc.Get("Debug.Job.RunProcess"),
            _ => _loc.Get("Debug.Job.Generic"),
        };

        return string.Format(_loc.Get("Debug.Notification.JobBlocked"), requestedJob, activeName);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
