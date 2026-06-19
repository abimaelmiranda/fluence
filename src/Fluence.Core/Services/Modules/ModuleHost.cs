using System;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Core.Services.Modules;

public sealed class ModuleHost(
    IServiceProvider services,
    IWorkspaceContext workspace,
    IShellEventBus eventBus,
    IShellRegionHost shellRegions) : IModuleHost
{
    public IServiceProvider Services { get; } = services;

    public IWorkspaceContext Workspace { get; } = workspace;

    public IShellEventBus Events { get; } = eventBus;

    public IShellRegionHost ShellRegions { get; } = shellRegions;

    public void SetModuleState(string moduleId, ModuleState state)
    {
        Workspace.SetModuleState(moduleId, state);
    }
}
