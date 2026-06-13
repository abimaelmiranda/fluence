using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Ports;
using Fluence.Core.Workspace;
using Fluence.Core.Modules;

namespace Fluence.Modules.Debug;

public sealed class DebugProjectCommandHandler(
    IProjectExecutionTargetResolver projectTargets,
    IDebugSessionManager sessions,
    IUserNotificationService notifications,
    IShellRegionHost shellRegions)
    : ICommandHandler<DebugProjectCommand>
{
    public async Task HandleAsync(DebugProjectCommand command, CancellationToken cancellationToken = default)
    {
        var target = await projectTargets.ResolveProjectTargetAsync(ExecutionMode.Debug, cancellationToken);
        if (target is null)
        {
            notifications.ShowWarning(
                "Debug",
                "No debuggable project was found for the active document.");
            return;
        }

        sessions.Start(target, ExecutionMode.Debug);
        shellRegions.Expand(ShellRegion.BottomBar);
        notifications.ShowWarning(
            "Debug",
            "Debug target resolved. The debug adapter is not implemented yet.");
    }
}
