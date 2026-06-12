using Fluence.Core.Workspace;

namespace Fluence.Core.Modules;

public sealed class ModuleHost(IWorkspaceContext workspace) : IModuleHost
{
    public IWorkspaceContext Workspace { get; } = workspace;

    public void SetModuleState(string moduleName, ModuleState state)
    {
        Workspace.SetModuleState(moduleName, state);
    }
}
