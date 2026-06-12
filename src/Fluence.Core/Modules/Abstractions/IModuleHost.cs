using System;
using Fluence.Core.Workspace;

namespace Fluence.Core.Modules;

public interface IModuleHost
{
    IServiceProvider Services { get; }

    IWorkspaceContext Workspace { get; }

    IShellEventBus Events { get; }

    IShellRegionHost ShellRegions { get; }

    void SetModuleState(string moduleName, ModuleState state);
}
