using Fluence.Core.Modules;
using Fluence.Core.Workspace;
using Fluence.Modules.Terminal.ViewModels;
using Fluence.Modules.Terminal.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.Terminal;

public sealed class TerminalModule : IIdeModule
{
    public string Name => "Terminal";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<TerminalViewModel>();
    }

    public void RegisterViews(IViewRegistry registry)
    {
        registry.Register<TerminalViewModel, TerminalView>();
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
