using System;
using System.Resources;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Keybindings;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Models.Keybindings;
using Fluence.Core.Services.Debugging;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.Debug.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Fluence.Modules.Debug.Commands.DebugProject;
using Fluence.Modules.Debug.Abstractions.Session;
using Fluence.Modules.Debug.Json;
using Fluence.Modules.Debug.Services;
using Fluence.Core.Events.Debug;

namespace Fluence.Modules.Debug;

public sealed class Entrypoint : IModule, IModuleShutdownParticipant
{
    private readonly List<IDisposable> _subscriptions = [];
    private IDebugService? _debugService;

    public string Id => "Debug";

    public string DisplayName => "Debug";

    public int StartupOrder => 1100;

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<DebugSidebarViewModel>();
        services.AddSingleton<IDebugStateService, DebugStateService>();
        services.AddSingleton<IDebugService, DebugService>();
        services.AddSingleton<IDebugSessionManager, DebugSessionManager>();
        services.AddSingleton<ICommandHandler<DebugProjectCommand>, DebugProjectCommandHandler>();
    }

    public ModuleContributions GetContributions() =>
        new()
        {
            Panels =
            [
                new ShellPanelContribution(
                    ShellRegion.Sidebar,
                    "DebugSidebar",
                    "Debug",
                    services => services.GetRequiredService<DebugSidebarViewModel>(),
                    PanelVisibilityRule.Custom((_, activeTabId, services) =>
                        activeTabId == "Debug" &&
                        services.GetRequiredService<IDebugSessionManager>().CurrentSession is { IsActive: true })),
            ],
        };

    public Task InitializeAsync(IModuleHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scheduler = host.Services.GetRequiredService<ITaskScheduler>();
        var debug = host.Services.GetRequiredService<IDebugService>();
        _debugService = debug;
        var commands = host.Services.GetRequiredService<ICommandRegistry>();

        host.Services.GetRequiredService<ISettingsRegistry>()
            .Register(DebugSettingsJsonContext.Default.DebugSettings);
        host.Services.GetRequiredService<ISettingsService>()
            .Get<DebugSettings>();

        host.Services.GetRequiredService<ILocalizationService>()
            .Register(new ResourceManager("Fluence.Modules.Debug.Resources.Strings", typeof(Entrypoint).Assembly));

        // Iniciar sessão — Interactive: usuário espera resposta imediata
        _subscriptions.Add(host.Events.SubscribeSync<DebugProjectRequestedEvent>(_ =>
            scheduler.Schedule("debug.start", TaskPriority.Interactive,
                ct => HandleAsync(host, ct))));

        // Controles de sessão — Critical: inputs diretos do usuário no debugger
        _subscriptions.Add(host.Events.SubscribeSync<StopDebugRequestedEvent>(_ =>
            scheduler.Schedule("debug.stop", TaskPriority.Critical,
                _ => debug.StopAsync())));
        _subscriptions.Add(host.Events.SubscribeSync<ReloadDebugRequestedEvent>(_ =>
            scheduler.Schedule("debug.reload", TaskPriority.Critical,
                _ => debug.RestartAsync())));
        _subscriptions.Add(host.Events.SubscribeSync<ContinueDebugRequestedEvent>(_ =>
            scheduler.Schedule("debug.continue", TaskPriority.Critical,
                _ => debug.ContinueAsync())));
        _subscriptions.Add(host.Events.SubscribeSync<StepOverDebugRequestedEvent>(_ =>
            scheduler.Schedule("debug.step-over", TaskPriority.Critical,
                _ => debug.StepOverAsync())));
        _subscriptions.Add(host.Events.SubscribeSync<StepIntoDebugRequestedEvent>(_ =>
            scheduler.Schedule("debug.step-into", TaskPriority.Critical,
                _ => debug.StepIntoAsync())));
        _subscriptions.Add(host.Events.SubscribeSync<StepOutDebugRequestedEvent>(_ =>
            scheduler.Schedule("debug.step-out", TaskPriority.Critical,
                _ => debug.StepOutAsync())));
        _subscriptions.Add(host.Events.SubscribeSync<ToggleBreakpointRequestedEvent>(e =>
            scheduler.Schedule("debug.breakpoint", TaskPriority.Interactive,
                _ => debug.ToggleBreakpointAsync(e.FilePath, e.Line))));

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

        host.SetModuleState(Id, ModuleState.Active);
        return Task.CompletedTask;
    }

    private static async Task HandleAsync(IModuleHost host, CancellationToken ct)
    {
        var handler = host.Services.GetRequiredService<ICommandHandler<DebugProjectCommand>>();
        await handler.HandleAsync(new DebugProjectCommand(), ct);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var subscription in _subscriptions)
            subscription.Dispose();
        _subscriptions.Clear();

        var service = _debugService;
        _debugService = null;
        if (service is not null)
            await service.StopAsync().ConfigureAwait(false);
    }

    public Task StopAsync(ModuleShutdownContext context)
    {
        var service = _debugService;
        _debugService = null;
        return service?.StopAsync(context.CancellationToken) ?? Task.CompletedTask;
    }
}
