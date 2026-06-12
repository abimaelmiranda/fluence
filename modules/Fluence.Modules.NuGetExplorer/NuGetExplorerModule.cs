using Fluence.Core.Modules;
using Fluence.Core.Workspace;
using Fluence.Modules.NuGetExplorer.ViewModels;
using Fluence.Modules.NuGetExplorer.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.NuGetExplorer;

public sealed class NuGetExplorerModule : IIdeModule
{
    public string Name => "NuGetExplorer";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<NuGetExplorerViewModel>();
        services.AddSingleton<INuGetPackageSource, NuGetOrgPackageSource>();
        services.AddSingleton<INuGetProjectService, NuGetProjectService>();
        services.AddSingleton<PackageIconLoader>();
    }

    public void RegisterViews(IViewRegistry registry)
    {
        registry.Register<NuGetExplorerViewModel, NuGetExplorerView>();
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
