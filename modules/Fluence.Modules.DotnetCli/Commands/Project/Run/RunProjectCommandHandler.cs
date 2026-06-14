using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;
using Fluence.Core.Modules;
using Fluence.Core.Ports;
using Fluence.Core.Workspace;
using Fluence.Modules.DotnetCli.Services;

namespace Fluence.Modules.DotnetCli.Commands.Project.Run;

public sealed class RunProjectCommandHandler(
    ITerminalService terminal,
    RunTargetResolver runTargets,
    ILaunchSettingsCoordinator launchSettings,
    IWorkspaceContext workspace,
    IUserNotificationService notifications,
    IShellRegionHost shellRegions)
    : ICommandHandler<RunProjectCommand>
{
    public async Task HandleAsync(RunProjectCommand command, CancellationToken cancellationToken = default)
    {
        if (workspace.Current.Mode is WorkspaceMode.Folder or WorkspaceMode.Solution &&
            await launchSettings.EnsureAsync(cancellationToken) is null)
        {
            notifications.ShowWarning(
                "Run",
                "No startup project was configured for this workspace.");
            return;
        }

        var target = await runTargets.ResolveAsync(cancellationToken);
        if (target is null)
        {
            notifications.ShowWarning(
                "Run",
                "No executable project or C# file was found for the active document.");
            return;
        }

        shellRegions.Expand(ShellRegion.BottomBar);
        await terminal.ExecuteAsync(target.Command, target.WorkingDirectory, cancellationToken);
    }
}
