using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Debug;
using Fluence.Core.Modules.Abstractions;
using Fluence.Core.Modules;
using Fluence.Core.Workspace;
using Fluence.Modules.Debug.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.Debug;

public sealed class Entrypoint : IModule
{
    public string Name => "Debug";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<DebugSidebarViewModel>();
        services.AddSingleton<IDebugStateService, DebugStateService>();
        services.AddSingleton<IDebugService, DebugService>();
        services.AddSingleton<IDebugSessionManager, DebugSessionManager>();
        services.AddSingleton<ICommandHandler<DebugProjectCommand>, DebugProjectCommandHandler>();
    }

    public void Initialize(IModuleHost host)
    {
        host.Events.Subscribe<DebugProjectRequestedEvent>(_event => { _ = HandleAsync(host); });
        host.Events.Subscribe<StopDebugRequestedEvent>(_event => { _ = host.Services.GetRequiredService<IDebugService>().StopAsync(); });
        host.Events.Subscribe<ReloadDebugRequestedEvent>(_event => { _ = host.Services.GetRequiredService<IDebugService>().RestartAsync(); });
        host.Events.Subscribe<ContinueDebugRequestedEvent>(_event => { _ = host.Services.GetRequiredService<IDebugService>().ContinueAsync(); });
        host.Events.Subscribe<StepOverDebugRequestedEvent>(_event => { _ = host.Services.GetRequiredService<IDebugService>().StepOverAsync(); });
        host.Events.Subscribe<StepIntoDebugRequestedEvent>(_event => { _ = host.Services.GetRequiredService<IDebugService>().StepIntoAsync(); });
        host.Events.Subscribe<StepOutDebugRequestedEvent>(_event => { _ = host.Services.GetRequiredService<IDebugService>().StepOutAsync(); });
        host.Events.Subscribe<ToggleBreakpointRequestedEvent>(e => { _ = host.Services.GetRequiredService<IDebugService>().ToggleBreakpointAsync(e.FilePath, e.Line); });
        host.SetModuleState(Name, ModuleState.Active);
    }

    private static async Task HandleAsync(IModuleHost host)
    {
        var handler = host.Services.GetRequiredService<ICommandHandler<DebugProjectCommand>>();
        await handler.HandleAsync(new DebugProjectCommand());
    }
}
