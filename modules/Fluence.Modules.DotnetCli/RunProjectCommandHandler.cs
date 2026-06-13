using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;
using Fluence.Core.Modules;
using Fluence.Core.Ports;

namespace Fluence.Modules.DotnetCli;

public sealed class RunProjectCommandHandler(
    ITerminalService terminal,
    RunTargetResolver runTargets,
    IUserNotificationService notifications,
    IShellRegionHost shellRegions)
    : ICommandHandler<RunProjectCommand>
{
    public Task HandleAsync(RunProjectCommand command, CancellationToken cancellationToken = default)
    {
        var target = runTargets.Resolve();
        if (target is null)
        {
            notifications.ShowWarning(
                "Run",
                "No executable project or C# file was found for the active document.");
            return Task.CompletedTask;
        }

        shellRegions.Expand(ShellRegion.BottomBar);
        return terminal.ExecuteAsync(target.Command, target.WorkingDirectory, cancellationToken);
    }
}
