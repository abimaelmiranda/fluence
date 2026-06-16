using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Services.Debugging;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Services.Modules;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.Debug.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Fluence.Modules.Debug.Commands.DebugProject;
using Fluence.Modules.Debug.Abstractions.Session;
using Fluence.Modules.Debug.Services;

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
        host.Events.SubscribeAsync<DebugProjectRequestedEvent>(_event => HandleAsync(host));
        host.Events.SubscribeAsync<StopDebugRequestedEvent>(_event => host.Services.GetRequiredService<IDebugService>().StopAsync());
        host.Events.SubscribeAsync<ReloadDebugRequestedEvent>(_event => host.Services.GetRequiredService<IDebugService>().RestartAsync());
        host.Events.SubscribeAsync<ContinueDebugRequestedEvent>(_event => host.Services.GetRequiredService<IDebugService>().ContinueAsync());
        host.Events.SubscribeAsync<StepOverDebugRequestedEvent>(_event => host.Services.GetRequiredService<IDebugService>().StepOverAsync());
        host.Events.SubscribeAsync<StepIntoDebugRequestedEvent>(_event => host.Services.GetRequiredService<IDebugService>().StepIntoAsync());
        host.Events.SubscribeAsync<StepOutDebugRequestedEvent>(_event => host.Services.GetRequiredService<IDebugService>().StepOutAsync());
        host.Events.SubscribeAsync<ToggleBreakpointRequestedEvent>(e => host.Services.GetRequiredService<IDebugService>().ToggleBreakpointAsync(e.FilePath, e.Line));
        host.SetModuleState(Name, ModuleState.Active);
    }

    private static async Task HandleAsync(IModuleHost host)
    {
        var handler = host.Services.GetRequiredService<ICommandHandler<DebugProjectCommand>>();
        await handler.HandleAsync(new DebugProjectCommand());
    }
}
