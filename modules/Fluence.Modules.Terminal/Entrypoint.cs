using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Services.Modules;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
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