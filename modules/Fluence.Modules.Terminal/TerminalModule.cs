using Fluence.Core.Modules;
using Fluence.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.Terminal;

public sealed class TerminalModule : IIdeModule
{
    public string Name => "Terminal";

    public void Register(IServiceCollection services)
    {
    }

    public void Initialize(IModuleHost host)
    {
        host.SetModuleState(Name, ModuleState.Active);
    }
}
