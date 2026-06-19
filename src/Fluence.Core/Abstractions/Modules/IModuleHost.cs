using System;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Core.Abstractions.Modules;

public interface IModuleHost
{
    IServiceProvider Services { get; }

    IWorkspaceContext Workspace { get; }

    IShellEventBus Events { get; }

    IShellRegionHost ShellRegions { get; }

    void SetModuleState(string moduleId, ModuleState state);
}
