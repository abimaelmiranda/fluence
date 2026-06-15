using System.Threading.Tasks;
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
        host.Events.Subscribe<BuildWorkspaceRequestedEvent>(_event => { _ = HandleAsync(host, new BuildWorkspaceCommand()); });
        host.Events.Subscribe<RunProjectRequestedEvent>(_event => { _ = RunAsync(host); });
        host.Events.Subscribe<TestWorkspaceRequestedEvent>(_event => { _ = HandleAsync(host, new TestWorkspaceCommand()); });
        host.Events.Subscribe<RestoreWorkspaceRequestedEvent>(_event => { _ = HandleAsync(host, new RestoreWorkspaceCommand()); });
        host.Events.Subscribe<CleanWorkspaceRequestedEvent>(_event => { _ = HandleAsync(host, new CleanWorkspaceCommand()); });
        host.Events.Subscribe<BuildProjectRequestedEvent>(e => { _ = HandleAsync(host, new BuildProjectCommand(e.ProjectPath)); });
        host.Events.Subscribe<RunSpecificProjectRequestedEvent>(e => { _ = HandleAsync(host, new RunSpecificProjectCommand(e.ProjectPath)); });
        host.Events.Subscribe<TestProjectRequestedEvent>(e => { _ = HandleAsync(host, new TestProjectCommand(e.ProjectPath)); });
        host.Events.Subscribe<RestoreProjectRequestedEvent>(e => { _ = HandleAsync(host, new RestoreProjectCommand(e.ProjectPath)); });
        host.Events.Subscribe<CleanProjectRequestedEvent>(e => { _ = HandleAsync(host, new CleanProjectCommand(e.ProjectPath)); });
        host.SetModuleState(Name, ModuleState.Active);
    }

    private static async Task HandleAsync<TCommand>(IModuleHost host, TCommand command)
    {
        host.ShellRegions.Expand(ShellRegion.BottomBar);
        var handler = host.Services.GetRequiredService<ICommandHandler<TCommand>>();
        await handler.HandleAsync(command);
    }

    private static async Task RunAsync(IModuleHost host)
    {
        var handler = host.Services.GetRequiredService<ICommandHandler<RunProjectCommand>>();
        await handler.HandleAsync(new RunProjectCommand());
    }
}
