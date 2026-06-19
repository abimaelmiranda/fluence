using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Modules.Debug.ViewModels;
using Fluence.Modules.Debug.Abstractions.Session;
using Fluence.Modules.Debug.Models;
using Fluence.Core.Events.Ui;

namespace Fluence.Modules.Debug.Services;

public sealed class DebugSessionManager(
    IWorkspaceContext workspace,
    IShellRegionHost shellRegions,
    IShellEventBus events,
    DebugSidebarViewModel sidebar,
    IUiDispatcher dispatcher)
    : IDebugSessionManager
{
    public DebugSession? CurrentSession { get; private set; }

    public void Start(ProjectExecutionTarget target, ExecutionMode mode)
    {
        if (!dispatcher.CheckAccess())
        {
            dispatcher.Post(() => Start(target, mode));
            return;
        }

        var architecture = target.Configuration?.Architecture;
        CurrentSession = new DebugSession(
            IsActive: true,
            ActiveMode: mode,
            TargetArchitecture: string.IsNullOrWhiteSpace(architecture) ? "x64" : architecture,
            ProcessId: null,
            Target: target);

        sidebar.Update(CurrentSession);
        workspace.SetMode(WorkspaceMode.Debugging);
        events.Publish(new ActivityBarTabSelectRequestedEvent("Debug"));
    }

    public void Stop()
    {
        if (!dispatcher.CheckAccess())
        {
            dispatcher.Post(Stop);
            return;
        }

        CurrentSession = null;
        sidebar.Clear();
        shellRegions.Refresh();

        var previousMode = workspace.Current.ModeBeforeDebugging;
        if (workspace.Current.Mode == WorkspaceMode.Debugging && previousMode is not null)
            workspace.SetMode(previousMode.Value);
    }
}
