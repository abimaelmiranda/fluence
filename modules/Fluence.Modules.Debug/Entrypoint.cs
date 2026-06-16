using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Keybindings;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Models.Keybindings;
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
        host.Events.SubscribeSync<DebugProjectRequestedEvent>(_event => { _ = HandleAsync(host); });
        host.Events.SubscribeAsync<StopDebugRequestedEvent>(_event => host.Services.GetRequiredService<IDebugService>().StopAsync());
        host.Events.SubscribeAsync<ReloadDebugRequestedEvent>(_event => host.Services.GetRequiredService<IDebugService>().RestartAsync());
        host.Events.SubscribeAsync<ContinueDebugRequestedEvent>(_event => host.Services.GetRequiredService<IDebugService>().ContinueAsync());
        host.Events.SubscribeAsync<StepOverDebugRequestedEvent>(_event => host.Services.GetRequiredService<IDebugService>().StepOverAsync());
        host.Events.SubscribeAsync<StepIntoDebugRequestedEvent>(_event => host.Services.GetRequiredService<IDebugService>().StepIntoAsync());
        host.Events.SubscribeAsync<StepOutDebugRequestedEvent>(_event => host.Services.GetRequiredService<IDebugService>().StepOutAsync());
        host.Events.SubscribeAsync<ToggleBreakpointRequestedEvent>(e => host.Services.GetRequiredService<IDebugService>().ToggleBreakpointAsync(e.FilePath, e.Line));

        var commands = host.Services.GetRequiredService<ICommandRegistry>();
        var debug = host.Services.GetRequiredService<IDebugService>();

        commands.Register(new IdeCommandDefinition(
            CommandIds.DebugContinue, "Continue", KeybindingScope.Global, "F5",
            _ => debug.ContinueAsync()));
        commands.Register(new IdeCommandDefinition(
            CommandIds.DebugStepOver, "Step Over", KeybindingScope.Global, "F10",
            _ => debug.StepOverAsync()));
        commands.Register(new IdeCommandDefinition(
            CommandIds.DebugStepInto, "Step Into", KeybindingScope.Global, "F11",
            _ => debug.StepIntoAsync()));
        commands.Register(new IdeCommandDefinition(
            CommandIds.DebugStepOut, "Step Out", KeybindingScope.Global, "Shift+F11",
            _ => debug.StepOutAsync()));
        host.SetModuleState(Name, ModuleState.Active);
    }

    private static async Task HandleAsync(IModuleHost host)
    {
        var handler = host.Services.GetRequiredService<ICommandHandler<DebugProjectCommand>>();
        await handler.HandleAsync(new DebugProjectCommand());
    }
}
