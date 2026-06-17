using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Services.Modules;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.DotnetCli.Commands;
using Fluence.Modules.DotnetCli.Models;
using Fluence.Modules.DotnetCli.Services;
using Fluence.Modules.DotnetCli.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Fluence.Modules.DotnetCli.Commands.Project.Run;
using Fluence.Modules.DotnetCli.Commands.Project.Clean;
using Fluence.Modules.DotnetCli.Commands.Project.Test;
using Fluence.Modules.DotnetCli.Commands.Project.Build;
using Fluence.Modules.DotnetCli.Commands.Project.Restore;
using Fluence.Modules.DotnetCli.Commands.Workspace.Restore;
using Fluence.Modules.DotnetCli.Commands.Workspace.Clean;
using Fluence.Modules.DotnetCli.Commands.Workspace.Build;
using Fluence.Modules.DotnetCli.Commands.Workspace.Test;

namespace Fluence.Modules.DotnetCli;

public sealed class Entrypoint : IModule
{
    public string Name => "DotnetCli";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IProjectExecutionTargetResolver, DotnetProjectExecutionTargetResolver>();
        services.AddSingleton<RunTargetResolver>();
        services.AddSingleton<NewProjectWizardViewModel>();
        services.AddSingleton<PublishWizardViewModel>();
        services.AddSingleton<DotnetSdkSetupViewModel>();
        services.AddSingleton<ICommandHandler<BuildWorkspaceCommand>, BuildWorkspaceCommandHandler>();
        services.AddSingleton<ICommandHandler<RunProjectCommand>, RunProjectCommandHandler>();
        services.AddSingleton<ICommandHandler<TestWorkspaceCommand>, TestWorkspaceCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreWorkspaceCommand>, RestoreWorkspaceCommandHandler>();
        services.AddSingleton<ICommandHandler<CleanWorkspaceCommand>, CleanWorkspaceCommandHandler>();
        services.AddSingleton<ICommandHandler<BuildProjectCommand>, BuildProjectCommandHandler>();
        services.AddSingleton<ICommandHandler<RunSpecificProjectCommand>, RunSpecificProjectCommandHandler>();
        services.AddSingleton<ICommandHandler<TestProjectCommand>, TestProjectCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreProjectCommand>, RestoreProjectCommandHandler>();
        services.AddSingleton<ICommandHandler<CleanProjectCommand>, CleanProjectCommandHandler>();
    }

    public void Initialize(IModuleHost host)
    {
        var scheduler = host.Services.GetRequiredService<ITaskScheduler>();

        // Build/Test/Restore/Clean são pesadas — Maintenance para não competir com o editor
        host.Events.SubscribeSync<BuildWorkspaceRequestedEvent>(_ =>
            scheduler.Schedule("dotnet.build", TaskPriority.Maintenance,
                ct => HandleAsync(host, new BuildWorkspaceCommand(), ct)));
        host.Events.SubscribeSync<TestWorkspaceRequestedEvent>(_ =>
            scheduler.Schedule("dotnet.test", TaskPriority.Maintenance,
                ct => HandleAsync(host, new TestWorkspaceCommand(), ct)));
        host.Events.SubscribeSync<RestoreWorkspaceRequestedEvent>(_ =>
            scheduler.Schedule("dotnet.restore", TaskPriority.Maintenance,
                ct => HandleAsync(host, new RestoreWorkspaceCommand(), ct)));
        host.Events.SubscribeSync<CleanWorkspaceRequestedEvent>(_ =>
            scheduler.Schedule("dotnet.clean", TaskPriority.Maintenance,
                ct => HandleAsync(host, new CleanWorkspaceCommand(), ct)));
        host.Events.SubscribeSync<BuildProjectRequestedEvent>(e =>
            scheduler.Schedule("dotnet.build", TaskPriority.Maintenance,
                ct => HandleAsync(host, new BuildProjectCommand(e.ProjectPath), ct)));
        host.Events.SubscribeSync<TestProjectRequestedEvent>(e =>
            scheduler.Schedule("dotnet.test", TaskPriority.Maintenance,
                ct => HandleAsync(host, new TestProjectCommand(e.ProjectPath), ct)));
        host.Events.SubscribeSync<RestoreProjectRequestedEvent>(e =>
            scheduler.Schedule("dotnet.restore", TaskPriority.Maintenance,
                ct => HandleAsync(host, new RestoreProjectCommand(e.ProjectPath), ct)));
        host.Events.SubscribeSync<CleanProjectRequestedEvent>(e =>
            scheduler.Schedule("dotnet.clean", TaskPriority.Maintenance,
                ct => HandleAsync(host, new CleanProjectCommand(e.ProjectPath), ct)));

        // Run é Interactive — usuário quer feedback imediato
        host.Events.SubscribeSync<RunProjectRequestedEvent>(_ =>
            scheduler.Schedule("dotnet.run", TaskPriority.Interactive,
                ct => RunAsync(host, ct)));
        host.Events.SubscribeSync<RunSpecificProjectRequestedEvent>(e =>
            scheduler.Schedule("dotnet.run", TaskPriority.Interactive,
                ct => HandleAsync(host, new RunSpecificProjectCommand(e.ProjectPath), ct)));

        // UI-only: abrem tool tabs, sem trabalho pesado
        host.Events.SubscribeSync<NewProjectRequestedEvent>(_ => OpenNewProjectWizard(host));
        host.Events.SubscribeSync<PublishProjectRequestedEvent>(e => OpenPublishWizard(host, e.ProjectPath));
        host.Events.SubscribeSync<DotnetSdkSetupRequestedEvent>(_ => OpenSdkSetup(host));

        host.SetModuleState(Name, ModuleState.Active);
    }

    private static void OpenNewProjectWizard(IModuleHost host)
    {
        var vm = host.Services.GetRequiredService<NewProjectWizardViewModel>();
        host.Workspace.OpenToolTab(ToolTabIds.NewProject, ToolTabIds.NewProjectTitle, vm);
    }

    private static void OpenPublishWizard(IModuleHost host, string? projectPath)
    {
        var vm = host.Services.GetRequiredService<PublishWizardViewModel>();
        vm.Load(projectPath);
        host.Workspace.OpenToolTab(ToolTabIds.Publish, ToolTabIds.PublishTitle, vm);
    }

    private static void OpenSdkSetup(IModuleHost host)
    {
        var vm = host.Services.GetRequiredService<DotnetSdkSetupViewModel>();
        host.Workspace.OpenToolTab(ToolTabIds.DotnetSdkSetup, ToolTabIds.DotnetSdkSetupTitle, vm);
        _ = vm.RefreshAsync();
    }

    private static async Task HandleAsync<TCommand>(IModuleHost host, TCommand command, CancellationToken ct)
    {
        if (!await EnsureSdkAsync(host).ConfigureAwait(false))
            return;

        host.Events.Publish(new ExpandPanelEvent("Terminal"));
        var handler = host.Services.GetRequiredService<ICommandHandler<TCommand>>();
        await handler.HandleAsync(command, ct);
    }

    private static async Task RunAsync(IModuleHost host, CancellationToken ct)
    {
        if (!await EnsureSdkAsync(host).ConfigureAwait(false))
            return;

        var handler = host.Services.GetRequiredService<ICommandHandler<RunProjectCommand>>();
        await handler.HandleAsync(new RunProjectCommand(), ct);
    }

    private static async Task<bool> EnsureSdkAsync(IModuleHost host)
    {
        var sdk = host.Services.GetRequiredService<IDotnetSdkProvisioningService>();
        var status = await sdk.GetStatusAsync().ConfigureAwait(false);
        if (status.IsDotnetAvailable && status.InstalledSdks.Count > 0)
            return true;

        host.Events.Publish(new DotnetSdkSetupRequestedEvent());
        return false;
    }
}
