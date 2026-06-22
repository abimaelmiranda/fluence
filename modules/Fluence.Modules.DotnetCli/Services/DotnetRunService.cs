using System;
using System.Collections.Generic;
using System.IO;
using Fluence.Core.Abstractions.Dialogs;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Projects;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Output;
using Fluence.Core.Models.Workbench;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Core.Events.Ui;

namespace Fluence.Modules.DotnetCli.Services;

public sealed class DotnetRunService(
    IProcessHost processHost,
    IOutputChannelService output,
    RunTargetResolver runTargets,
    IDotnetSdkProvisioningService sdk,
    ILaunchSettingsCoordinator launchSettings,
    IWorkspaceContext workspace,
    IUserNotificationService notifications,
    IShellEventBus events)
    : IRunService
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (workspace.Current.Mode is WorkspaceMode.Folder or WorkspaceMode.Solution &&
            await launchSettings.EnsureAsync(ExecutionMode.Release, cancellationToken).ConfigureAwait(false) is null)
        {
            notifications.ShowWarning(
                "Run",
                "No startup project was configured for this workspace.");
            return;
        }

        var target = await runTargets.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            notifications.ShowWarning(
                "Run",
                "No executable project or C# file was found for the active document.");
            return;
        }

        events.Publish(new SelectBottomBarTabEvent(BottomBarTabIds.Run));
        output.Clear(OutputChannelIds.Run);
        await output.WriteAsync(
            OutputChannelIds.Run,
            $"> {DotnetCommandLine.Format(target.Executable, target.Arguments)}{Environment.NewLine}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await processHost.RunAsync(
            target.Executable,
            target.Arguments,
            target.WorkingDirectory,
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine),
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine, OutputChannelEntryKind.Error),
            cancellationToken,
            environment: target.Environment).ConfigureAwait(false);
    }

    public async Task RunSpecificAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        events.Publish(new SelectBottomBarTabEvent(BottomBarTabIds.Run));
        output.Clear(OutputChannelIds.Run);

        var dotnet = await sdk.ResolveDotnetExecutableAsync(cancellationToken).ConfigureAwait(false);
        var arguments = new List<string> { "run", "--project", projectPath };
        var workingDirectory = Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory();
        await output.WriteAsync(
            OutputChannelIds.Run,
            $"> {DotnetCommandLine.Format(dotnet, arguments)}{Environment.NewLine}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await processHost.RunAsync(
            dotnet,
            arguments,
            workingDirectory,
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine),
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine, OutputChannelEntryKind.Error),
            cancellationToken,
            environment: null).ConfigureAwait(false);
    }
}
