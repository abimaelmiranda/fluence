using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Modules;
using Fluence.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.Debug;

public sealed class DebugModule : IIdeModule
{
    public string Name => "Debug";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IDebugSessionManager, DebugSessionManager>();
        services.AddSingleton<ICommandHandler<DebugProjectCommand>, DebugProjectCommandHandler>();
    }

    public void Initialize(IModuleHost host)
    {
        host.Events.Subscribe<DebugProjectRequestedEvent>(_event => { _ = HandleAsync(host); });
        host.SetModuleState(Name, ModuleState.Active);
    }

    private static async Task HandleAsync(IModuleHost host)
    {
        var handler = host.Services.GetRequiredService<ICommandHandler<DebugProjectCommand>>();
        await handler.HandleAsync(new DebugProjectCommand());
    }
}
