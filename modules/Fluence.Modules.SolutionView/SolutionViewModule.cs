using Fluence.Core.Modules;
using Fluence.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.SolutionView;

public sealed class SolutionViewModule : IIdeModule
{
    public string Name => "SolutionView";

    public void Register(IServiceCollection services)
    {
    }

    public void Initialize(IModuleHost host)
    {
        host.SetModuleState(Name, ModuleState.Active);
    }
}
