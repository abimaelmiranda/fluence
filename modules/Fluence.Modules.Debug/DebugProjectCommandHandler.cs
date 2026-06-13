using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Ports;
using Fluence.Core.Workspace;

namespace Fluence.Modules.Debug;

public sealed class DebugProjectCommandHandler(
    IProjectExecutionTargetResolver projectTargets,
    IDebugSessionManager sessions,
    IUserNotificationService notifications)
    : ICommandHandler<DebugProjectCommand>
{
    public Task HandleAsync(DebugProjectCommand command, CancellationToken cancellationToken = default)
    {
        var target = projectTargets.ResolveProjectTarget();
        if (target is null)
        {
            notifications.ShowWarning(
                "Debug",
                "No debuggable project was found for the active document.");
            return Task.CompletedTask;
        }

        sessions.Prepare(target);
        notifications.ShowWarning(
            "Debug",
            "Debug target resolved. The debug adapter is not implemented yet.");
        return Task.CompletedTask;
    }
}
