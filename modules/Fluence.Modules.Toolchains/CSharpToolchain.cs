using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Jobs;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Abstractions.Toolchains;
using Fluence.Core.Events.Toolchains;
using Fluence.Core.Events.Ui;
using Fluence.Core.Models.Jobs;
using Fluence.Core.Models.Toolchains;
using Fluence.Core.Models.Workbench;
using Fluence.Modules.Debug.Commands.DebugProject;
using Fluence.Modules.DotnetCli.Commands;
using Fluence.Modules.DotnetCli.Commands.Project.Build;
using Fluence.Modules.DotnetCli.Commands.Project.Clean;
using Fluence.Modules.DotnetCli.Commands.Project.Restore;
using Fluence.Modules.DotnetCli.Commands.Project.Run;
using Fluence.Modules.DotnetCli.Commands.Project.Test;
using Fluence.Modules.DotnetCli.Commands.Workspace.Build;
using Fluence.Modules.DotnetCli.Commands.Workspace.Clean;
using Fluence.Modules.DotnetCli.Commands.Workspace.Restore;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.Toolchains;

public sealed class CSharpToolchain(
    IServiceProvider services,
    IShellEventBus events) : IToolchain
{
    public string Id => "csharp";

    public string LanguageId => "csharp";

    public ToolchainSupportLevel SupportLevel => ToolchainSupportLevel.Primary;

    public ToolchainCapabilities Capabilities { get; } = new(
        ToolchainCapability.Sdk |
        ToolchainCapability.Build |
        ToolchainCapability.Run |
        ToolchainCapability.Test |
        ToolchainCapability.Restore |
        ToolchainCapability.Clean |
        ToolchainCapability.LanguageServer |
        ToolchainCapability.Debugger);

    public async Task<bool> EnsureAsync(
        ToolchainCapability capability,
        CancellationToken cancellationToken = default)
    {
        if (capability.HasFlag(ToolchainCapability.Sdk) && !await HasSdkAsync(cancellationToken).ConfigureAwait(false))
        {
            events.Publish(new ToolchainProvisioningRequiredEvent(Id, ToolchainCapability.Sdk));
            return false;
        }

        if (capability.HasFlag(ToolchainCapability.LanguageServer) &&
            !services.GetRequiredService<ILspProvisioningService>().IsProvisioned())
        {
            events.Publish(new ToolchainProvisioningRequiredEvent(Id, ToolchainCapability.LanguageServer));
            return false;
        }

        if (capability.HasFlag(ToolchainCapability.Debugger) &&
            !services.GetRequiredService<IDebuggerProvisioningService>().IsProvisioned())
        {
            events.Publish(new ToolchainProvisioningRequiredEvent(Id, ToolchainCapability.Debugger));
            return false;
        }

        return true;
    }

    public async Task ExecuteAsync(ToolchainCommand command, CancellationToken cancellationToken = default)
    {
        switch (command.Kind)
        {
            case ToolchainCommandKind.BuildWorkspace:
                await HandleAsync(new BuildWorkspaceCommand(), cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.TestWorkspace:
                await HandleAsync(new TestWorkspaceCommand(), cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.RestoreWorkspace:
                await HandleAsync(new RestoreWorkspaceCommand(), cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.CleanWorkspace:
                await HandleAsync(new CleanWorkspaceCommand(), cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.BuildProject:
                await HandleAsync(new BuildProjectCommand(RequireProjectPath(command)), cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.TestProject:
                await HandleAsync(new TestProjectCommand(RequireProjectPath(command)), cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.RestoreProject:
                await HandleAsync(new RestoreProjectCommand(RequireProjectPath(command)), cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.CleanProject:
                await HandleAsync(new CleanProjectCommand(RequireProjectPath(command)), cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.RunProject:
                await RunExclusiveAsync(() =>
                    services.GetRequiredService<ICommandHandler<RunProjectCommand>>()
                        .HandleAsync(new RunProjectCommand(), cancellationToken)).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.RunSpecificProject:
                await RunExclusiveAsync(() =>
                    services.GetRequiredService<ICommandHandler<RunSpecificProjectCommand>>()
                        .HandleAsync(new RunSpecificProjectCommand(RequireProjectPath(command)), cancellationToken)).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.DebugProject:
                if (await EnsureAsync(ToolchainCapability.Sdk | ToolchainCapability.Debugger, cancellationToken).ConfigureAwait(false))
                    await services.GetRequiredService<ICommandHandler<DebugProjectCommand>>()
                        .HandleAsync(new DebugProjectCommand(), cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.StartLanguageServer:
                await EnsureAsync(ToolchainCapability.LanguageServer, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleAsync<TCommand>(TCommand command, CancellationToken cancellationToken)
    {
        if (!await EnsureAsync(ToolchainCapability.Sdk, cancellationToken).ConfigureAwait(false))
            return;

        events.Publish(new SelectBottomBarTabEvent(BottomBarTabIds.Run));
        await services.GetRequiredService<ICommandHandler<TCommand>>()
            .HandleAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RunExclusiveAsync(Func<Task> run)
    {
        if (!await EnsureAsync(ToolchainCapability.Sdk).ConfigureAwait(false))
            return;

        var jobs = services.GetRequiredService<IExclusiveJobCoordinator>();
        if (!jobs.TryAcquire(ExclusiveJobKind.Run, out var lease))
        {
            var activeName = jobs.ActiveJob switch
            {
                ExclusiveJobKind.Debug => "debug session",
                ExclusiveJobKind.Run => "run process",
                _ => "job",
            };
            services.GetRequiredService<IUserNotificationService>()
                .ShowWarning("Run", $"Cannot start run while a {activeName} is running.");
            return;
        }

        using (lease!)
        {
            events.Publish(new SelectBottomBarTabEvent(BottomBarTabIds.Run));
            await run().ConfigureAwait(false);
        }
    }

    private async Task<bool> HasSdkAsync(CancellationToken cancellationToken)
    {
        var status = await services.GetRequiredService<IDotnetSdkProvisioningService>()
            .GetStatusAsync(cancellationToken)
            .ConfigureAwait(false);
        return status.IsDotnetAvailable && status.InstalledSdks.Count > 0;
    }

    private static string RequireProjectPath(ToolchainCommand command) =>
        string.IsNullOrWhiteSpace(command.ProjectPath)
            ? throw new ArgumentException("Project path is required.", nameof(command))
            : command.ProjectPath;
}
