using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Keybindings;
using Fluence.Core.Abstractions.Tasks;
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
        var scheduler = host.Services.GetRequiredService<ITaskScheduler>();
        var debug = host.Services.GetRequiredService<IDebugService>();
        var commands = host.Services.GetRequiredService<ICommandRegistry>();

        // Iniciar sessão — Interactive: usuário espera resposta imediata
        host.Events.SubscribeSync<DebugProjectRequestedEvent>(_ =>
            scheduler.Schedule("debug.start", TaskPriority.Interactive,
                ct => HandleAsync(host, ct)));

        // Controles de sessão — Critical: inputs diretos do usuário no debugger
        host.Events.SubscribeSync<StopDebugRequestedEvent>(_ =>
            scheduler.Schedule("debug.stop", TaskPriority.Critical,
                _ => debug.StopAsync()));
        host.Events.SubscribeSync<ReloadDebugRequestedEvent>(_ =>
            scheduler.Schedule("debug.reload", TaskPriority.Critical,
                _ => debug.RestartAsync()));
        host.Events.SubscribeSync<ContinueDebugRequestedEvent>(_ =>
            scheduler.Schedule("debug.continue", TaskPriority.Critical,
                _ => debug.ContinueAsync()));
        host.Events.SubscribeSync<StepOverDebugRequestedEvent>(_ =>
            scheduler.Schedule("debug.step-over", TaskPriority.Critical,
                _ => debug.StepOverAsync()));
        host.Events.SubscribeSync<StepIntoDebugRequestedEvent>(_ =>
            scheduler.Schedule("debug.step-into", TaskPriority.Critical,
                _ => debug.StepIntoAsync()));
        host.Events.SubscribeSync<StepOutDebugRequestedEvent>(_ =>
            scheduler.Schedule("debug.step-out", TaskPriority.Critical,
                _ => debug.StepOutAsync()));
        host.Events.SubscribeSync<ToggleBreakpointRequestedEvent>(e =>
            scheduler.Schedule("debug.breakpoint", TaskPriority.Interactive,
                _ => debug.ToggleBreakpointAsync(e.FilePath, e.Line)));

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

    private static async Task HandleAsync(IModuleHost host, CancellationToken ct)
    {
        var handler = host.Services.GetRequiredService<ICommandHandler<DebugProjectCommand>>();
        await handler.HandleAsync(new DebugProjectCommand(), ct);
    }
}
