using System;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
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
    private IDisposable? _managePackagesSubscription;

    public string Id => "NuGetExplorer";

    public string DisplayName => "NuGet Explorer";

    public int StartupOrder => 500;

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<NuGetExplorerViewModel>();
        services.AddSingleton<INuGetPackageSource, NuGetOrgPackageSource>();
        services.AddSingleton<INuGetProjectService, NuGetProjectService>();
        services.AddSingleton<PackageIconLoader>();
    }

    public Task InitializeAsync(IModuleHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _managePackagesSubscription = host.Events.SubscribeSync<ManageNuGetPackagesRequestedEvent>(e =>
        {
            host.Services.GetRequiredService<NuGetExplorerViewModel>().OpenForSolution(e.SolutionPath);
        });
        host.SetModuleState(Id, ModuleState.Active);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _managePackagesSubscription?.Dispose();
        _managePackagesSubscription = null;
        return ValueTask.CompletedTask;
    }
}
