using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Jobs;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Projects;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Events.Debug;
using Fluence.Core.Events.Provisioning;
using Fluence.Core.Events.Ui;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Models.Jobs;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Models.Output;
using Fluence.Core.Models.Workbench;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Debugging;
using Fluence.Core.Services.Workspace;
using Fluence.Infrastructure.Projects;

namespace Fluence.Infrastructure.Protocols.Dap;

public sealed class CppDebugService(
    IWorkspaceContext workspace,
    IDebugAdapterClientFactory adapterFactory,
    IDebuggerProvisioningService provisioning,
    IDebugStateService debugState,
    IOutputChannelService output,
    IProcessHost processHost,
    IUserNotificationService notifications,
    IShellRegionHost shellRegions,
    IShellEventBus events,
    IExclusiveJobCoordinator jobs,
    ILocalizationService localization) : IDebugService, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IDebugAdapterClient? _adapter;
    private IExclusiveJobLease? _jobLease;
    private bool _adapterStarted;
    private int _sessionGeneration;
    private readonly ILocalizationService _loc = localization;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!provisioning.IsProvisioned())
        {
            events.Publish(new DebuggerProvisioningRequiredEvent());
            return;
        }

        try { await _gate.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (ObjectDisposedException) { return; }
        try
        {
            if (_adapter is not null)
            {
                ShowWarning("A debug session is already running.");
                return;
            }

            if (!jobs.TryAcquire(ExclusiveJobKind.Debug, out var lease))
            {
                ShowWarning(FormatJobBlockedMessage(jobs.ActiveJob, "debug session"));
                return;
            }

            _jobLease = lease;
            var context = CppProjectLocator.ResolveFromWorkspace(workspace);
            if (context is null)
            {
                ShowWarning("No C or C++ project root was found.");
                ReleaseDebugJobLease();
                return;
            }

            if (!context.HasCMakeLists)
            {
                ShowWarning("Only CMake-based C and C++ projects are supported for debug right now.");
                ReleaseDebugJobLease();
                return;
            }

            events.Publish(new SelectBottomBarTabEvent(BottomBarTabIds.Run));
            if (!await ConfigureAndBuildAsync(context, cancellationToken).ConfigureAwait(false))
            {
                ReleaseDebugJobLease();
                return;
            }

            var executable = CppProjectLocator.FindExecutablePath(context.BuildDirectory, context);
            if (executable is null)
            {
                ShowWarning("The native executable could not be located after building.");
                ReleaseDebugJobLease();
                return;
            }

            var workspaceRoot = context.ProjectRoot;
            _adapter = await adapterFactory.CreateAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
            _adapter.Stopped += OnAdapterStopped;
            _adapter.Terminated += OnAdapterTerminated;
            _adapter.Continued += OnAdapterContinued;
            _adapter.OutputReceived += OnAdapterOutputReceived;

            var launchConfiguration = new LaunchConfiguration
            {
                Architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            };

            var request = new DebugLaunchRequest(
                ProjectPath: context.ProjectRoot,
                ProgramPath: executable,
                WorkingDirectory: context.ProjectRoot,
                Configuration: launchConfiguration,
                LaunchArguments: CreateLaunchArguments(executable, context.ProjectRoot),
                Breakpoints: debugState.Snapshot.Breakpoints,
                WorkspaceRoot: workspaceRoot);

            debugState.StartSession(RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
            workspace.SetMode(WorkspaceMode.Debugging);
            events.Publish(new ActivityBarTabSelectRequestedEvent("Debug"));
            SelectSidebar();
            SelectBottomBar(BottomBarTabIds.Debug);
            Interlocked.Increment(ref _sessionGeneration);

            await _adapter.StartAsync(request, cancellationToken).ConfigureAwait(false);
            await SyncBreakpointsAsync(debugState.Snapshot.Breakpoints, cancellationToken).ConfigureAwait(false);
            await _adapter.SetExceptionBreakpointsAsync(DebugExceptionBreakMode.OnlyUserUnhandled, cancellationToken).ConfigureAwait(false);
            await _adapter.CompleteConfigurationAsync(cancellationToken).ConfigureAwait(false);
            _adapterStarted = true;
            debugState.Continue();
            await output.WriteAsync(OutputChannelIds.Debug, "[debug] Session started\r\n").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ShowWarning(ex.Message);
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try { await _gate.WaitAsync(cancellationToken).ConfigureAwait(false); }
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
        await StopAsync(cancellationToken).ConfigureAwait(false);
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
            await SyncBreakpointsAsync(breakpoints, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DebugVariable>> GetVariablesAsync(int variablesReference, CancellationToken cancellationToken = default)
    {
        var adapter = _adapter;
        if (adapter is null)
            return [];

        try
        {
            return await adapter.GetVariablesAsync(variablesReference, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return [];
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async Task<bool> ConfigureAndBuildAsync(CppProjectContext context, CancellationToken cancellationToken)
    {
        var cmake = PlatformTooling.Current.FindOnPath("cmake");
        if (cmake is null)
        {
            ShowWarning("cmake was not found on PATH.");
            return false;
        }

        var buildDir = context.BuildDirectory;
        Directory.CreateDirectory(buildDir);

        var configureArgs = new List<string>
        {
            "-S", context.ProjectRoot,
            "-B", buildDir,
            "-DCMAKE_EXPORT_COMPILE_COMMANDS=ON",
            "-DCMAKE_BUILD_TYPE=Debug",
        };

        await output.WriteAsync(OutputChannelIds.Run, $"> {FormatCommand(cmake, configureArgs)}{Environment.NewLine}", cancellationToken: cancellationToken).ConfigureAwait(false);
        var configureResult = await processHost.RunWithResultAsync(
            cmake,
            configureArgs,
            context.ProjectRoot,
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine),
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine, OutputChannelEntryKind.Error),
            cancellationToken).ConfigureAwait(false);
        if (!configureResult.Succeeded)
            return false;

        var buildArgs = new List<string> { "--build", buildDir, "--config", "Debug" };
        await output.WriteAsync(OutputChannelIds.Run, $"> {FormatCommand(cmake, buildArgs)}{Environment.NewLine}", cancellationToken: cancellationToken).ConfigureAwait(false);
        var buildResult = await processHost.RunWithResultAsync(
            cmake,
            buildArgs,
            context.ProjectRoot,
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine),
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine, OutputChannelEntryKind.Error),
            cancellationToken).ConfigureAwait(false);
        return buildResult.Succeeded;
    }

    private async Task SyncBreakpointsAsync(IReadOnlyList<DebugBreakpoint> breakpoints, CancellationToken cancellationToken)
    {
        var adapter = _adapter;
        if (adapter is null)
            return;

        var verificationByFile = await adapter.SetBreakpointsAsync(breakpoints, cancellationToken).ConfigureAwait(false);
        foreach (var group in verificationByFile)
            debugState.SetBreakpointVerification(group.Key, group.Value.ToDictionary(b => b.Line));
    }

    private async Task RunAdapterCommandAsync(Func<IDebugAdapterClient, Task> command)
    {
        var adapter = _adapter;
        if (adapter is null)
            return;

        try
        {
            await command(adapter).ConfigureAwait(false);
        }
        catch
        {
            ShowWarning("The debug adapter did not respond to the command in time.");
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
        _ = RefreshInspectionAsync(e, sessionGeneration, CancellationToken.None);
    }

    private void OnAdapterTerminated(object? sender, DebugAdapterTerminatedEvent e)
    {
        if (!_adapterStarted)
            return;

        _ = StopAsync(CancellationToken.None);
    }

    private void OnAdapterContinued(object? sender, DebugAdapterContinuedEvent e)
    {
        debugState.Continue();
    }

    private void OnAdapterOutputReceived(object? sender, DebugAdapterOutputEvent e)
    {
        _ = output.WriteAsync(OutputChannelIds.Debug, e.Text, e.IsError ? OutputChannelEntryKind.Error : OutputChannelEntryKind.Information);
    }

    private void ShowWarning(string message) =>
        notifications.ShowWarning("Debug", message);

    private void SelectBottomBar(string tabId) =>
        events.Publish(new SelectBottomBarTabEvent(tabId));

    private void SelectSidebar() =>
        shellRegions.Expand(ShellRegion.Sidebar);

    private static string FormatJobBlockedMessage(ExclusiveJobKind? activeJob, string jobName) =>
        activeJob is null ? $"Cannot start {jobName}." : $"Cannot start {jobName} while {activeJob} is running.";

    private void ReleaseDebugJobLease()
    {
        _jobLease?.Dispose();
        _jobLease = null;
    }

    private static JsonObject CreateLaunchArguments(string programPath, string workingDirectory)
    {
        return new JsonObject
        {
            ["type"] = "lldb-dap",
            ["program"] = programPath,
            ["cwd"] = workingDirectory,
            ["stopOnEntry"] = false,
            ["args"] = new JsonArray(),
        };
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

            var currentFrame = SelectStoppedFrame(frames);
            var variables = currentFrame is null
                ? Array.Empty<DebugVariable>()
                : await adapter.GetVariablesAsync(currentFrame.Id, cancellationToken).ConfigureAwait(false);
            if (sessionGeneration != Volatile.Read(ref _sessionGeneration))
                return;

            var currentLine = currentFrame is { FilePath: { Length: > 0 }, Line: > 0 }
                ? new DebugExecutionLine(currentFrame.FilePath!, currentFrame.Line)
                : null;

            var exceptionInfo = IsExceptionStop(e) ? CreateExceptionInfo(e) : null;
            debugState.SetStopped(e.Reason, e.ThreadId, currentLine, exceptionInfo);
            debugState.SetInspectionData(frames, variables);

            if (currentLine is not null)
                await output.WriteAsync(OutputChannelIds.Debug, $"[debug] stopped at {currentLine.FilePath}:{currentLine.Line}\r\n").ConfigureAwait(false);
            else
                await output.WriteAsync(OutputChannelIds.Debug, $"[debug] stopped: {e.Reason}\r\n").ConfigureAwait(false);

            if (exceptionInfo is not null)
                notifications.ShowError("Debug", exceptionInfo.Message);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await output.WriteAsync(OutputChannelIds.Debug, $"[debug] failed to inspect stop: {ex.Message}\r\n", OutputChannelEntryKind.Error).ConfigureAwait(false);
        }
    }

    private static DebugStackFrame? SelectStoppedFrame(IReadOnlyList<DebugStackFrame> frames) =>
        frames.FirstOrDefault(frame => !string.IsNullOrWhiteSpace(frame.FilePath) && frame.Line > 0)
        ?? frames.FirstOrDefault();

    private static bool IsExceptionStop(DebugAdapterStoppedEvent e) =>
        string.Equals(e.Reason, "exception", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(e.Reason, "pause", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(e.Description);

    private DebugExceptionInfo CreateExceptionInfo(DebugAdapterStoppedEvent e) =>
        new(
            _loc.Get("Debug.Exception.Title"),
            string.IsNullOrWhiteSpace(e.Description)
                ? e.Text ?? _loc.Get("Debug.Exception.Message")
                : e.Description!);

    private static string FormatCommand(string executable, IReadOnlyList<string> arguments) =>
        "\"" + executable.Replace("\"", "\\\"", StringComparison.Ordinal) + "\" " +
        string.Join(" ", arguments.Select(arg => "\"" + arg.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""));
}
