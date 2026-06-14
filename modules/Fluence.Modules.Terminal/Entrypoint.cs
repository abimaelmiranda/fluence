using Fluence.Core.Modules.Abstractions;
using Fluence.Core.Modules;
using Fluence.Core.Workspace;
using Fluence.Modules.Terminal.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.Terminal;

public sealed class Entrypoint : IModule
{
    public string Name => "Terminal";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<TerminalViewModel>();
    }

    public void Initialize(IModuleHost host)
    {
        host.ShellRegions.SetContent(
            ShellRegion.BottomBar,
            Name,
            "Terminal",
            host.Services.GetRequiredService<TerminalViewModel>());
        host.SetModuleState(Name, ModuleState.Active);
    }
}