using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Services.Modules;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.NuGetExplorer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Fluence.Modules.NuGetExplorer.Services;
using Fluence.Modules.NuGetExplorer.Abstractions;

namespace Fluence.Modules.NuGetExplorer;

public sealed class Entrypoint : IModule
{
    public string Name => "NuGetExplorer";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<NuGetExplorerViewModel>();
        services.AddSingleton<INuGetPackageSource, NuGetOrgPackageSource>();
        services.AddSingleton<INuGetProjectService, NuGetProjectService>();
        services.AddSingleton<PackageIconLoader>();
    }

    public void Initialize(IModuleHost host)
    {
        host.Events.Subscribe<ManageNuGetPackagesRequestedEvent>(e =>
        {
            host.Services.GetRequiredService<NuGetExplorerViewModel>().OpenForSolution(e.SolutionPath);
        });
        host.SetModuleState(Name, ModuleState.Active);
    }
}
