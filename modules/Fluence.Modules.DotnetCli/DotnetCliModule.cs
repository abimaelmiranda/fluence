using Fluence.Core.Modules;
using Fluence.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.DotnetCli;

public sealed class DotnetCliModule : IIdeModule
{
    public string Name => "DotnetCli";

    public void Register(IServiceCollection services)
    {
    }

    public void Initialize(IModuleHost host)
    {
        host.SetModuleState(Name, ModuleState.Active);
    }
}
