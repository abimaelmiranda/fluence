using Fluence.Core.Modules;
using Fluence.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.Editor;

public sealed class EditorModule : IIdeModule
{
    public string Name => "Editor";

    public void Register(IServiceCollection services)
    {
    }

    public void Initialize(IModuleHost host)
    {
        host.SetModuleState(Name, ModuleState.Active);
    }
}
