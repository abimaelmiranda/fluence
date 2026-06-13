using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Modules;
using Fluence.Core.Workspace;
using Fluence.Modules.Debug.ViewModels;
using Fluence.Modules.Debug.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.Debug;

public sealed class DebugModule : IIdeModule
{
    public string Name => "Debug";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<DebugSidebarViewModel>();
        services.AddSingleton<IDebugSessionManager, DebugSessionManager>();
        services.AddSingleton<ICommandHandler<DebugProjectCommand>, DebugProjectCommandHandler>();
    }

    public void RegisterViews(IViewRegistry registry)
    {
        registry.Register<DebugSidebarViewModel, DebugSidebarView>();
    }

    public void Initialize(IModuleHost host)
    {
        host.Events.Subscribe<DebugProjectRequestedEvent>(_event => { _ = HandleAsync(host); });
        host.Events.Subscribe<StopDebugRequestedEvent>(_event => host.Services.GetRequiredService<IDebugSessionManager>().Stop());
        host.SetModuleState(Name, ModuleState.Active);
    }

    private static async Task HandleAsync(IModuleHost host)
    {
        var handler = host.Services.GetRequiredService<ICommandHandler<DebugProjectCommand>>();
        await handler.HandleAsync(new DebugProjectCommand());
    }
}
