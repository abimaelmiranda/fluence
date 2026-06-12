using Fluence.Core.Modules;
using Fluence.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.FileExplorer;

public sealed class FileExplorerModule : IIdeModule
{
    public string Name => "FileExplorer";

    public void Register(IServiceCollection services)
    {
    }

    public void Initialize(IModuleHost host)
    {
        host.SetModuleState(Name, ModuleState.Active);
    }
}
