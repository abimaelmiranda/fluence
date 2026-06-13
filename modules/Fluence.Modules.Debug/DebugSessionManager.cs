using Fluence.Core.Workspace;
using Fluence.Core.Modules;
using Fluence.Modules.Debug.ViewModels;

namespace Fluence.Modules.Debug;

public sealed class DebugSessionManager(
    IWorkspaceContext workspace,
    IShellRegionHost shellRegions,
    DebugSidebarViewModel sidebar)
    : IDebugSessionManager
{
    public DebugSession? CurrentSession { get; private set; }

    public void Start(ProjectExecutionTarget target, ExecutionMode mode)
    {
        var architecture = target.Configuration?.Architecture;
        CurrentSession = new DebugSession(
            IsActive: true,
            ActiveMode: mode,
            StartupProjectId: target.ProjectPath,
            TargetArchitecture: string.IsNullOrWhiteSpace(architecture) ? "x64" : architecture,
            ProcessId: null,
            Target: target);

        sidebar.Update(CurrentSession);
        workspace.SetMode(WorkspaceMode.Debugging);
        shellRegions.SetContent(ShellRegion.Sidebar, "DebugSidebar", "Debug", sidebar);
    }

    public void Stop()
    {
        CurrentSession = null;
        sidebar.Clear();
        shellRegions.ClearContent(ShellRegion.Sidebar, "DebugSidebar");

        var previousMode = workspace.Current.ModeBeforeDebugging;
        if (workspace.Current.Mode == WorkspaceMode.Debugging && previousMode is not null)
            workspace.SetMode(previousMode.Value);
    }
}
