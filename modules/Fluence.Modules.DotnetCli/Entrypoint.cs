using System.Threading.Tasks;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Modules;
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
        host.Events.SubscribeAsync<BuildWorkspaceRequestedEvent>(_event => HandleAsync(host, new BuildWorkspaceCommand()));
        host.Events.SubscribeSync<RunProjectRequestedEvent>(_event => { _ = RunAsync(host); });
        host.Events.SubscribeAsync<TestWorkspaceRequestedEvent>(_event => HandleAsync(host, new TestWorkspaceCommand()));
        host.Events.SubscribeAsync<RestoreWorkspaceRequestedEvent>(_event => HandleAsync(host, new RestoreWorkspaceCommand()));
        host.Events.SubscribeAsync<CleanWorkspaceRequestedEvent>(_event => HandleAsync(host, new CleanWorkspaceCommand()));
        host.Events.SubscribeAsync<BuildProjectRequestedEvent>(e => HandleAsync(host, new BuildProjectCommand(e.ProjectPath)));
        host.Events.SubscribeAsync<RunSpecificProjectRequestedEvent>(e => HandleAsync(host, new RunSpecificProjectCommand(e.ProjectPath)));
        host.Events.SubscribeAsync<TestProjectRequestedEvent>(e => HandleAsync(host, new TestProjectCommand(e.ProjectPath)));
        host.Events.SubscribeAsync<RestoreProjectRequestedEvent>(e => HandleAsync(host, new RestoreProjectCommand(e.ProjectPath)));
        host.Events.SubscribeAsync<CleanProjectRequestedEvent>(e => HandleAsync(host, new CleanProjectCommand(e.ProjectPath)));
        host.Events.SubscribeSync<NewProjectRequestedEvent>(_ => OpenNewProjectWizard(host));
        host.Events.SubscribeSync<PublishProjectRequestedEvent>(e => OpenPublishWizard(host, e.ProjectPath));
        host.Events.SubscribeSync<DotnetSdkSetupRequestedEvent>(_ => OpenSdkSetup(host));
        host.SetModuleState(Name, ModuleState.Active);
    }

    private static async Task HandleAsync<TCommand>(IModuleHost host, TCommand command)
    {
        if (!await EnsureSdkAsync(host).ConfigureAwait(false))
            return;

        host.Events.Publish(new ExpandPanelEvent("Terminal"));
        var handler = host.Services.GetRequiredService<ICommandHandler<TCommand>>();
        await handler.HandleAsync(command);
    }

    private static async Task RunAsync(IModuleHost host)
    {
        if (!await EnsureSdkAsync(host).ConfigureAwait(false))
            return;

        var handler = host.Services.GetRequiredService<ICommandHandler<RunProjectCommand>>();
        await handler.HandleAsync(new RunProjectCommand());
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
