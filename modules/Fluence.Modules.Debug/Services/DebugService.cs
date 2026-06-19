using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Threading;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Services.Debugging;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Jobs;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Services.Modules;
using Fluence.Core.Abstractions.Dialogs;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.File;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Problems;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Models.Output;
using Fluence.Core.Models.Jobs;
using Fluence.Core.Models.Problems;
using Fluence.Core.Models.Workbench;
using Fluence.Core.Services.File;
using Fluence.Core.Services.Problems;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.Debug.Abstractions.Session;

namespace Fluence.Modules.Debug;

public sealed class DebugService(
    IProjectExecutionTargetResolver projectTargets,
    ILaunchSettingsService launchSettings,
    IWorkspaceContext workspace,
    IDebugAdapterClientFactory adapterFactory,
    IDebugStateService debugState,
    IOutputChannelService output,
    IProcessHost processHost,
    IUserNotificationService notifications,
    IShellRegionHost shellRegions,
    IShellEventBus events,
    IDebugSessionManager sessions,
    IDebuggerProvisioningService provisioning,
    IDotnetSdkProvisioningService sdk,
    IProblemService problems,
    ISettingsService settings,
    ITaskScheduler scheduler,
    IExclusiveJobCoordinator jobs)
    : IDebugService, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IDebugAdapterClient? _adapter;
    private IExclusiveJobLease? _jobLease;
    private bool _adapterStarted;
    private int _sessionGeneration;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!provisioning.IsProvisioned())
        {
            events.Publish(new DebuggerProvisioningRequiredEvent());
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_adapter is not null)
            {
                ShowWarning("A debug session is already running.");
                return;
            }

            if (!jobs.TryAcquire(ExclusiveJobKind.Debug, out var lease))
            {
                ShowWarning(FormatJobBlockedMessage(jobs.ActiveJob, "debug"));
                return;
            }

            _jobLease = lease;

            var target = await projectTargets.ResolveProjectTargetAsync(ExecutionMode.Debug, cancellationToken).ConfigureAwait(false);
            if (target is null)
            {
                ShowWarning("No debuggable project was found for the active document.");
                ReleaseDebugJobLease();
                return;
            }

            var started = await StartSessionAsync(target, ExecutionMode.Debug, cancellationToken).ConfigureAwait(false);
            if (!started)
                ReleaseDebugJobLease();
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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var currentSession = sessions.CurrentSession;
            if (currentSession is null)
            {
                ShowWarning("No active debug session to reload.");
                return;
            }

            var target = currentSession.Target;
            var mode = currentSession.ActiveMode;
            await StopCoreAsync(cancellationToken, releaseJob: false).ConfigureAwait(false);
            var restarted = await StartSessionAsync(target, mode, cancellationToken).ConfigureAwait(false);
            if (!restarted)
                ReleaseDebugJobLease();
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
            await output.WriteAsync(OutputChannelIds.Debug, "[debug] Debug adapter did not respond to command in time.\r\n", OutputChannelEntryKind.Error).ConfigureAwait(false);
        }
    }

    private async Task<bool> StartSessionAsync(ProjectExecutionTarget target, ExecutionMode mode, CancellationToken cancellationToken)
    {
        output.Clear(OutputChannelIds.Debug);
        var workspaceRoot = launchSettings.GetWorkspaceRoot(workspace.Current);
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            ShowWarning("No workspace root was found for the debug session.");
            return false;
        }

        SelectBottomBar(BottomBarTabIds.Run);
        output.Clear(OutputChannelIds.Run);
        problems.ClearSource(ProblemSourceIds.Build);
        var buildProblems = new List<ProblemItem>();
        var buildProblemsGate = new object();

        var dotnet = await sdk.ResolveDotnetExecutableAsync(cancellationToken).ConfigureAwait(false);
        var buildArguments = new[] { "build", target.ProjectPath, "-c", "Debug" };
        var buildWorkingDirectory = Path.GetDirectoryName(target.ProjectPath);
        await output.WriteAsync(
            OutputChannelIds.Run,
            $"> {FormatCommand(dotnet, buildArguments)}\r\n",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var buildResult = await processHost.RunWithResultAsync(
            dotnet,
            buildArguments,
            buildWorkingDirectory,
            line => ScheduleRunOutput(line, buildWorkingDirectory, buildProblems, buildProblemsGate, isError: false),
            line => ScheduleRunOutput(line, buildWorkingDirectory, buildProblems, buildProblemsGate, isError: true),
            cancellationToken).ConfigureAwait(false);
        problems.ReplaceSource(ProblemSourceIds.Build, buildProblems);
        if (!buildResult.Succeeded || buildProblems.Any(problem => problem.Severity == ProblemSeverity.Error))
        {
            ShowWarning("The debug build failed. Fix the build errors before starting a debug session.");
            return false;
        }

        var programPath = ResolveProgramPath(target.ProjectPath, workspaceRoot);
        if (programPath is null)
        {
            ShowWarning("The debug build output DLL was not found.");
            return false;
        }

        sessions.Start(target, mode);
        debugState.StartSession();
        var exceptionBreakMode = settings.Get<DebugSettings>().ExceptionBreakMode;
        SelectBottomBar(BottomBarTabIds.Debug);

        Interlocked.Increment(ref _sessionGeneration);
        _adapter = await adapterFactory.CreateAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        _adapter.Stopped += OnAdapterStopped;
        _adapter.Terminated += OnAdapterTerminated;
        _adapter.Continued += OnAdapterContinued;
        _adapter.OutputReceived += OnAdapterOutputReceived;

        var configuration = await CreateDebugConfigurationAsync(target, cancellationToken).ConfigureAwait(false);
        var workingDirectory = ResolveWorkingDirectory(target.ProjectPath, workspaceRoot, configuration.WorkingDirectory);
        await output.WriteAsync(
            OutputChannelIds.Debug,
            $"[debug] program: {programPath}\r\n[debug] cwd: {workingDirectory}\r\n",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var request = new DebugLaunchRequest(
            ProjectPath: target.ProjectPath,
            ProgramPath: programPath,
            WorkingDirectory: workingDirectory,
            Configuration: configuration,
            Breakpoints: debugState.Snapshot.Breakpoints,
            WorkspaceRoot: workspaceRoot);
        await _adapter.StartAsync(request, cancellationToken).ConfigureAwait(false);
        await SyncBreakpointsAsync(debugState.Snapshot.Breakpoints, cancellationToken).ConfigureAwait(false);
        await _adapter.SetExceptionBreakpointsAsync(exceptionBreakMode, cancellationToken).ConfigureAwait(false);
        await _adapter.CompleteConfigurationAsync(cancellationToken).ConfigureAwait(false);
        _adapterStarted = true;
        debugState.Continue();
        await output.WriteAsync(OutputChannelIds.Debug, "[debug] Session started\r\n").ConfigureAwait(false);
        return true;
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
            sessions.Stop();
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

            var workspaceRoot = launchSettings.GetWorkspaceRoot(workspace.Current);
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

            var exceptionInfo = IsExceptionStop(e)
                ? CreateExceptionInfo(e)
                : null;
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
        catch (OperationCanceledException)
        {
            // Adapter disposed or timed out — session may have already ended
        }
        catch (Exception ex)
        {
            await output.WriteAsync(OutputChannelIds.Debug, $"[debug] Error refreshing inspection: {ex.Message}\r\n", OutputChannelEntryKind.Error)
                .ConfigureAwait(false);
        }
    }

    private void OnAdapterTerminated(object? sender, DebugAdapterTerminatedEvent e)
    {
        if (!_adapterStarted)
        {
            var hint = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)
                ? " On macOS this is often a PATH/dotnet issue (the .app inherits a minimal PATH) or a debugger entitlement issue (com.apple.security.cs.debugger). Check the DAP log for stderr output."
                : " Check the DAP log for details.";
            scheduler.Schedule(
                "debug.output",
                TaskPriority.Background,
                ct => output.WriteAsync(
                    OutputChannelIds.Debug,
                    $"[debug] netcoredbg terminated before starting the debuggee.{hint}\r\n",
                    OutputChannelEntryKind.Error,
                    ct));
        }
        scheduler.Schedule("debug.stop", TaskPriority.Critical, StopAsync);
    }

    private void OnAdapterContinued(object? sender, DebugAdapterContinuedEvent e)
    {
        debugState.Continue();
    }

    private void OnAdapterOutputReceived(object? sender, DebugAdapterOutputEvent e)
    {
        ScheduleDebugOutput(e.Text, e.IsError);
    }

    private void ScheduleDebugOutput(string text, bool isError)
    {
        scheduler.Schedule(
            "debug.output",
            isError ? TaskPriority.Interactive : TaskPriority.Background,
            ct => output.WriteAsync(
                OutputChannelIds.Debug,
                text,
                isError ? OutputChannelEntryKind.Error : OutputChannelEntryKind.Information,
                ct));
    }

    private void OpenStoppedFile(string filePath)
    {
        if (!File.Exists(filePath))
            return;

        if (Dispatcher.UIThread.CheckAccess())
        {
            events.Publish(new OpenFileRequestedEvent(filePath));
            return;
        }

        Dispatcher.UIThread.Post(() => events.Publish(new OpenFileRequestedEvent(filePath)));
    }

    private static bool IsExceptionStop(DebugAdapterStoppedEvent e) =>
        string.Equals(e.Reason, "exception", StringComparison.OrdinalIgnoreCase);

    private static DebugStackFrame? SelectStoppedFrame(
        IReadOnlyList<DebugStackFrame> frames,
        string? workspaceRoot)
    {
        return frames.FirstOrDefault(frame => IsWorkspaceSourceFrame(frame, workspaceRoot)) ??
               frames.FirstOrDefault(IsSourceFrame) ??
               frames.FirstOrDefault();
    }

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
        File.Exists(frame.FilePath);

    private static DebugExceptionInfo CreateExceptionInfo(DebugAdapterStoppedEvent e)
    {
        var title = FirstNonEmpty(e.Text, e.Description, "Exception");
        var message = FirstNonEmpty(e.Description, e.Text, "The debugger stopped on an exception.");

        if (string.Equals(title, message, StringComparison.Ordinal))
            message = "The debugger stopped on an exception.";

        return new DebugExceptionInfo(TrimForPopup(title, 120), TrimForPopup(message, 220));
    }

    private static string FormatStoppedOutput(DebugAdapterStoppedEvent e, DebugExceptionInfo? exceptionInfo)
    {
        if (exceptionInfo is null)
            return $"[debug] Stopped: {e.Reason ?? "breakpoint"}\r\n";

        return $"[debug] Exception: {exceptionInfo.Title} - {exceptionInfo.Message}\r\n";
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

    private async Task SyncBreakpointsAsync(
        IReadOnlyList<DebugBreakpoint> breakpoints,
        CancellationToken cancellationToken)
    {
        var adapter = _adapter;
        if (adapter is null)
            return;

        var verificationByFile = await adapter.SetBreakpointsAsync(breakpoints, cancellationToken).ConfigureAwait(false);
        foreach (var file in verificationByFile)
        {
            debugState.SetBreakpointVerification(
                file.Key,
                file.Value.ToDictionary(b => b.Line, b => b));

            foreach (var breakpoint in file.Value.Where(b => !b.IsVerified))
            {
                await output.WriteAsync(
                    OutputChannelIds.Debug,
                    $"[debug] Breakpoint pending: {Path.GetFileName(file.Key)}:{breakpoint.Line}\r\n")
                    .ConfigureAwait(false);
            }
        }
    }

    private void SelectBottomBar(string tabId)
    {
        events.Publish(new SelectBottomBarTabEvent(tabId));

        if (Dispatcher.UIThread.CheckAccess())
        {
            shellRegions.Expand(ShellRegion.BottomBar);
            return;
        }

        Dispatcher.UIThread.Post(() => shellRegions.Expand(ShellRegion.BottomBar));
    }

    private void ScheduleRunOutput(
        string line,
        string? workingDirectory,
        List<ProblemItem> buildProblems,
        object buildProblemsGate,
        bool isError)
    {
        var problem = MsBuildProblemParser.TryParse(line, workingDirectory);
        if (problem is not null)
        {
            lock (buildProblemsGate)
                buildProblems.Add(problem);
        }

        scheduler.Schedule(
            "run.output",
            isError ? TaskPriority.Interactive : TaskPriority.Background,
            ct => output.WriteAsync(
                OutputChannelIds.Run,
                line + Environment.NewLine,
                isError ? OutputChannelEntryKind.Error : OutputChannelEntryKind.Information,
                ct));
    }

    private static string FormatCommand(string executable, IReadOnlyList<string> arguments) =>
        string.Join(" ", new[] { QuoteIfNeeded(executable) }.Concat(arguments.Select(QuoteIfNeeded)));

    private static string QuoteIfNeeded(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        return value.Any(char.IsWhiteSpace)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }

    private void ShowWarning(string message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            notifications.ShowWarning("Debug", message);
            return;
        }

        Dispatcher.UIThread.Post(() => notifications.ShowWarning("Debug", message));
    }

    private void ShowError(string message)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            notifications.ShowError("Debug", message);
            return;
        }

        Dispatcher.UIThread.Post(() => notifications.ShowError("Debug", message));
    }

    private void ReleaseDebugJobLease()
    {
        _jobLease?.Dispose();
        _jobLease = null;
    }

    private static string FormatJobBlockedMessage(ExclusiveJobKind? activeJob, string requestedJob)
    {
        var activeName = activeJob switch
        {
            ExclusiveJobKind.Debug => "debug session",
            ExclusiveJobKind.Run => "run process",
            _ => "job",
        };

        return $"Cannot start {requestedJob} while a {activeName} is running.";
    }

    private static string? ResolveProgramPath(string projectPath, string workspaceRoot)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
            return null;

        var document = XDocument.Load(projectPath);
        var targetFramework = FindProperty(document, "TargetFramework") ??
                              FindProperty(document, "TargetFrameworks")?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(targetFramework))
            return null;

        var assemblyName = FindProperty(document, "AssemblyName") ??
                           Path.GetFileNameWithoutExtension(projectPath);
        var candidate = Path.Combine(projectDirectory, "bin", "Debug", targetFramework, assemblyName + ".dll");
        if (File.Exists(candidate))
            return candidate;

        return FindBuildOutput(projectDirectory, workspaceRoot, assemblyName, targetFramework);
    }

    private static string? FindBuildOutput(
        string projectDirectory,
        string workspaceRoot,
        string assemblyName,
        string targetFramework)
    {
        var fileName = assemblyName + ".dll";
        return EnumerateSearchRoots(projectDirectory, workspaceRoot)
            .Where(Directory.Exists)
            .SelectMany(root => SafeEnumerateFiles(root, fileName))
            .Where(path => IsDebugBuildOutput(path, targetFramework))
            .OrderByDescending(HasRuntimeConfig)
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static IEnumerable<string> EnumerateSearchRoots(string projectDirectory, string workspaceRoot)
    {
        yield return projectDirectory;

        if (!string.Equals(projectDirectory, workspaceRoot, StringComparison.OrdinalIgnoreCase))
            yield return workspaceRoot;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root, string fileName)
    {
        try
        {
            return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return [];
        }
    }

    private static bool IsDebugBuildOutput(string path, string targetFramework)
    {
        return path.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
               path.Contains($"{Path.DirectorySeparatorChar}{targetFramework}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasRuntimeConfig(string assemblyPath)
    {
        var runtimeConfigPath = Path.Combine(
            Path.GetDirectoryName(assemblyPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(assemblyPath) + ".runtimeconfig.json");
        return File.Exists(runtimeConfigPath);
    }

    private static string? FindProperty(XDocument document, string name)
    {
        return document
            .Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            ?.Value
            .Trim();
    }

    private async Task<LaunchConfiguration> CreateDebugConfigurationAsync(
        ProjectExecutionTarget target,
        CancellationToken cancellationToken)
    {
        var source = target.Configuration ?? new LaunchConfiguration();
        var configuration = new LaunchConfiguration
        {
            Name = source.Name,
            RunProfileName = source.RunProfileName,
            DebugProfileName = source.DebugProfileName,
            Architecture = source.Architecture,
            WorkingDirectory = source.WorkingDirectory,
            Args = source.Args.ToList(),
            Env = new Dictionary<string, string>(source.Env, StringComparer.OrdinalIgnoreCase),
        };

        var profile = await ResolveDebugProfileAsync(target, cancellationToken).ConfigureAwait(false);
        if (profile is not null)
        {
            foreach (var item in profile.EnvironmentVariables)
                configuration.Env[item.Key] = item.Value;

            if (!string.IsNullOrWhiteSpace(profile.ApplicationUrl) &&
                !configuration.Env.ContainsKey("ASPNETCORE_URLS"))
                configuration.Env["ASPNETCORE_URLS"] = profile.ApplicationUrl;

            if (!string.IsNullOrWhiteSpace(profile.CommandLineArgs))
                configuration.Args.InsertRange(0, SplitCommandLine(profile.CommandLineArgs));

            if (!string.IsNullOrWhiteSpace(profile.WorkingDirectory))
                configuration.WorkingDirectory = profile.WorkingDirectory;

            if (!configuration.Env.ContainsKey("ASPNETCORE_ENVIRONMENT"))
                configuration.Env["ASPNETCORE_ENVIRONMENT"] = "Development";
            if (!configuration.Env.ContainsKey("DOTNET_ENVIRONMENT"))
                configuration.Env["DOTNET_ENVIRONMENT"] = "Development";
        }

        return configuration;
    }

    private async Task<DotnetLaunchProfile?> ResolveDebugProfileAsync(
        ProjectExecutionTarget target,
        CancellationToken cancellationToken)
    {
        var profileName = target.Configuration?.DebugProfileName;
        if (string.IsNullOrWhiteSpace(profileName))
            return null;

        var profiles = await launchSettings.DiscoverLaunchProfilesAsync(target.ProjectPath, cancellationToken).ConfigureAwait(false);
        return profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, profileName, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveWorkingDirectory(
        string projectPath,
        string workspaceRoot,
        string? configuredWorkingDirectory)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath) ?? workspaceRoot;
        if (string.IsNullOrWhiteSpace(configuredWorkingDirectory))
            return projectDirectory;

        return Path.IsPathRooted(configuredWorkingDirectory)
            ? configuredWorkingDirectory
            : Path.GetFullPath(Path.Combine(projectDirectory, configuredWorkingDirectory));
    }

    private static IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < commandLine.Length; i++)
        {
            var ch = commandLine[i];
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                AddCurrent();
                continue;
            }

            current.Append(ch);
        }

        AddCurrent();
        return args;

        void AddCurrent()
        {
            if (current.Length == 0)
                return;

            args.Add(current.ToString());
            current.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

}
