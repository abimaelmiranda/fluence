using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Threading;
using Fluence.Core.Debug;
using Fluence.Core.Infrastructure;
using Fluence.Core.Modules;
using Fluence.Core.Ports;
using Fluence.Core.Workspace;

namespace Fluence.Modules.Debug;

public sealed class DebugService(
    IProjectExecutionTargetResolver projectTargets,
    ILaunchSettingsService launchSettings,
    IWorkspaceContext workspace,
    IDebugAdapterClientFactory adapterFactory,
    IDebugStateService debugState,
    ITerminalService terminal,
    IProcessHost processHost,
    IUserNotificationService notifications,
    IShellRegionHost shellRegions,
    IShellEventBus events,
    IDebugSessionManager sessions,
    IDebuggerProvisioningService provisioning)
    : IDebugService, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IDebugAdapterClient? _adapter;
    private bool _adapterStarted;

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
                await StopCoreAsync(cancellationToken).ConfigureAwait(false);

            var target = await projectTargets.ResolveProjectTargetAsync(ExecutionMode.Debug, cancellationToken).ConfigureAwait(false);
            if (target is null)
            {
                ShowWarning("No debuggable project was found for the active document.");
                return;
            }

            await StartSessionAsync(target, ExecutionMode.Debug, cancellationToken).ConfigureAwait(false);
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
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
            await StartSessionAsync(target, mode, cancellationToken).ConfigureAwait(false);
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
            await terminal.WriteOutputAsync("[debug] Process is running; step/continue is available after a stop event.\r\n").ConfigureAwait(false);
            return;
        }

        debugState.Continue();
        try
        {
            await command(adapter).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await terminal.WriteOutputAsync("[debug] Debug adapter did not respond to command in time.\r\n", isError: true).ConfigureAwait(false);
        }
    }

    private async Task StartSessionAsync(ProjectExecutionTarget target, ExecutionMode mode, CancellationToken cancellationToken)
    {
        var workspaceRoot = launchSettings.GetWorkspaceRoot(workspace.Current);
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            ShowWarning("No workspace root was found for the debug session.");
            return;
        }

        ExpandBottomBar();
        await terminal.WriteOutputAsync("[debug] Building project...\r\n", cancellationToken: cancellationToken).ConfigureAwait(false);
        await processHost.RunAsync(
            "dotnet",
            $"build \"{target.ProjectPath}\" -c Debug",
            Path.GetDirectoryName(target.ProjectPath),
            line => _ = terminal.WriteOutputAsync(line + Environment.NewLine),
            line => _ = terminal.WriteOutputAsync(line + Environment.NewLine, isError: true),
            cancellationToken).ConfigureAwait(false);

        var programPath = ResolveProgramPath(target.ProjectPath, workspaceRoot);
        if (programPath is null)
        {
            ShowWarning("The debug build output DLL was not found.");
            return;
        }

        sessions.Start(target, mode);
        debugState.StartSession();

        _adapter = await adapterFactory.CreateAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        _adapter.Stopped += OnAdapterStopped;
        _adapter.Terminated += OnAdapterTerminated;
        _adapter.Continued += OnAdapterContinued;
        _adapter.OutputReceived += OnAdapterOutputReceived;

        var request = new DebugLaunchRequest(
            ProjectPath: target.ProjectPath,
            ProgramPath: programPath,
            WorkingDirectory: Path.GetDirectoryName(target.ProjectPath) ?? workspaceRoot,
            Configuration: target.Configuration ?? new LaunchConfiguration(),
            Breakpoints: debugState.Snapshot.Breakpoints,
            WorkspaceRoot: workspaceRoot);
        await _adapter.StartAsync(request, cancellationToken).ConfigureAwait(false);
        await SyncBreakpointsAsync(debugState.Snapshot.Breakpoints, cancellationToken).ConfigureAwait(false);
        await _adapter.CompleteConfigurationAsync(cancellationToken).ConfigureAwait(false);
        _adapterStarted = true;
        debugState.Continue();
        await terminal.WriteOutputAsync("[debug] Session started\r\n").ConfigureAwait(false);
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
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
    }

    private void OnAdapterStopped(object? sender, DebugAdapterStoppedEvent e)
    {
        _ = RefreshInspectionAsync(e);
    }

    private async Task RefreshInspectionAsync(DebugAdapterStoppedEvent e)
    {
        try
        {
            var adapter = _adapter;
            if (adapter is null)
                return;

            var frames = await adapter.GetStackTraceAsync(e.ThreadId).ConfigureAwait(false);
            var currentFrame = frames.FirstOrDefault();
            var variables = currentFrame is null
                ? Array.Empty<DebugVariable>()
                : await adapter.GetVariablesAsync(currentFrame.Id).ConfigureAwait(false);

            var currentLine = !string.IsNullOrWhiteSpace(currentFrame?.FilePath) && currentFrame.Line > 0
                ? new DebugExecutionLine(currentFrame.FilePath!, currentFrame.Line)
                : null;
            if (currentLine is not null)
                OpenStoppedFile(currentLine.FilePath);

            debugState.SetStopped(e.Reason, e.ThreadId, currentLine);
            debugState.SetInspectionData(frames, variables);
            await terminal.WriteOutputAsync($"[debug] Stopped: {e.Reason ?? "breakpoint"}\r\n").ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Adapter disposed or timed out — session may have already ended
        }
        catch (Exception ex)
        {
            await terminal.WriteOutputAsync($"[debug] Error refreshing inspection: {ex.Message}\r\n", isError: true)
                .ConfigureAwait(false);
        }
    }

    private void OnAdapterTerminated(object? sender, DebugAdapterTerminatedEvent e)
    {
        if (!_adapterStarted)
        {
            var hint = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX)
                ? " On macOS this is usually a debugger entitlement issue (com.apple.security.cs.debugger). Check the DAP log for stderr output."
                : " Check the DAP log for details.";
            _ = terminal.WriteOutputAsync(
                $"[debug] netcoredbg terminated before starting the debuggee.{hint}\r\n",
                isError: true);
        }
        _ = StopAsync();
    }

    private void OnAdapterContinued(object? sender, DebugAdapterContinuedEvent e)
    {
        debugState.Continue();
    }

    private void OnAdapterOutputReceived(object? sender, DebugAdapterOutputEvent e)
    {
        _ = terminal.WriteOutputAsync(e.Text, e.IsError);
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
                await terminal.WriteOutputAsync(
                    $"[debug] Breakpoint pending: {Path.GetFileName(file.Key)}:{breakpoint.Line}\r\n")
                    .ConfigureAwait(false);
            }
        }
    }

    private void ExpandBottomBar()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            shellRegions.Expand(ShellRegion.BottomBar);
            return;
        }

        Dispatcher.UIThread.Post(() => shellRegions.Expand(ShellRegion.BottomBar));
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
            .OrderByDescending(File.GetLastWriteTimeUtc)
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
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
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

    private static string? FindProperty(XDocument document, string name)
    {
        return document
            .Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            ?.Value
            .Trim();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
