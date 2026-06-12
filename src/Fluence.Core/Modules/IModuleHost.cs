using Fluence.Core.Workspace;

namespace Fluence.Core.Modules;

public interface IModuleHost
{
    IWorkspaceContext Workspace { get; }

    void SetModuleState(string moduleName, ModuleState state);
}
