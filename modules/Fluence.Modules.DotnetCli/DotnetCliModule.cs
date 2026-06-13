using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Modules;
using Fluence.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.DotnetCli;

public sealed class DotnetCliModule : IIdeModule
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
        host.Events.Subscribe<BuildWorkspaceRequestedEvent>(_event => { _ = HandleAsync<BuildWorkspaceCommand>(host, new BuildWorkspaceCommand()); });
        host.Events.Subscribe<RunProjectRequestedEvent>(_event => { _ = RunAsync(host); });
        host.Events.Subscribe<TestWorkspaceRequestedEvent>(_event => { _ = HandleAsync<TestWorkspaceCommand>(host, new TestWorkspaceCommand()); });
        host.Events.Subscribe<RestoreWorkspaceRequestedEvent>(_event => { _ = HandleAsync<RestoreWorkspaceCommand>(host, new RestoreWorkspaceCommand()); });
        host.Events.Subscribe<CleanWorkspaceRequestedEvent>(_event => { _ = HandleAsync<CleanWorkspaceCommand>(host, new CleanWorkspaceCommand()); });
        host.Events.Subscribe<BuildProjectRequestedEvent>(e => { _ = HandleAsync<BuildProjectCommand>(host, new BuildProjectCommand(e.ProjectPath)); });
        host.Events.Subscribe<RunSpecificProjectRequestedEvent>(e => { _ = HandleAsync<RunSpecificProjectCommand>(host, new RunSpecificProjectCommand(e.ProjectPath)); });
        host.Events.Subscribe<TestProjectRequestedEvent>(e => { _ = HandleAsync<TestProjectCommand>(host, new TestProjectCommand(e.ProjectPath)); });
        host.Events.Subscribe<RestoreProjectRequestedEvent>(e => { _ = HandleAsync<RestoreProjectCommand>(host, new RestoreProjectCommand(e.ProjectPath)); });
        host.Events.Subscribe<CleanProjectRequestedEvent>(e => { _ = HandleAsync<CleanProjectCommand>(host, new CleanProjectCommand(e.ProjectPath)); });
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
