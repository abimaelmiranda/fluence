using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Models.Output;
using Fluence.Core.Models.Workbench;
using Fluence.Core.Abstractions.Dialogs;
using Fluence.Core.Abstractions.File;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Services.File;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.DotnetCli.Services;

namespace Fluence.Modules.DotnetCli.Commands.Project.Run;

public sealed class RunProjectCommandHandler(
    IProcessHost processHost,
    IOutputChannelService output,
    RunTargetResolver runTargets,
    ILaunchSettingsCoordinator launchSettings,
    IWorkspaceContext workspace,
    IUserNotificationService notifications,
    IShellEventBus events)
    : ICommandHandler<RunProjectCommand>
{
    public async Task HandleAsync(RunProjectCommand command, CancellationToken cancellationToken = default)
    {
        output.Clear(OutputChannelIds.Run);

        if (workspace.Current.Mode is WorkspaceMode.Folder or WorkspaceMode.Solution &&
            await launchSettings.EnsureAsync(ExecutionMode.Release, cancellationToken) is null)
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

        events.Publish(new SelectBottomBarTabEvent(BottomBarTabIds.Run));
        await output.WriteAsync(
            OutputChannelIds.Run,
            $"> {DotnetCommandLine.Format(target.Executable, target.Arguments)}{Environment.NewLine}",
            cancellationToken: cancellationToken);
        await processHost.RunAsync(
            target.Executable,
            target.Arguments,
            target.WorkingDirectory,
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine),
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine, OutputChannelEntryKind.Error),
            cancellationToken,
            environment: target.Environment);
    }
}
