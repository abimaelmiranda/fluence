using System;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Core.Abstractions.Modules;

public interface IModuleHost
{
    IServiceProvider Services { get; }

    IWorkspaceContext Workspace { get; }

    IShellEventBus Events { get; }

    IShellRequestBus Requests { get; }

    IUiDispatcher Dispatcher { get; }

    IShellRegionHost ShellRegions { get; }

    void SetModuleState(string moduleId, ModuleState state);
}
