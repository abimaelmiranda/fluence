using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Services.Modules;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.SolutionView.Abstractions;
using Fluence.Modules.SolutionView.Commands;
using Fluence.Modules.SolutionView.Services;
using Fluence.Modules.SolutionView.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Fluence.Modules.SolutionView.Commands.OpenSolution;
using Fluence.Modules.SolutionView.Commands.SetStartupProject;
using Fluence.Modules.SolutionView.Commands.AddProjectReference;
using Fluence.Modules.SolutionView.Commands.RemoveProjectReference;

namespace Fluence.Modules.SolutionView;

public sealed class Entrypoint : IModule
{
    public string Name => "SolutionView";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<SolutionProjectAssociationService>();
        services.AddSingleton<IProjectAssociationService>(provider => provider.GetRequiredService<SolutionProjectAssociationService>());
        services.AddSingleton<SolutionViewModel>();
        services.AddSingleton<ISolutionWorkspaceLoader, BuildalyzerSolutionWorkspaceLoader>();
        services.AddSingleton<ISolutionStructureService, SolutionStructureService>();
        services.AddSingleton<IProjectReferenceService, ProjectReferenceService>();
        services.AddSingleton<IProjectReferenceDialogService, AvaloniaProjectReferenceDialogService>();
        services.AddSingleton<ISolutionFileCreationDialogService, AvaloniaSolutionFileCreationDialogService>();
        services.AddSingleton<ICommandHandler<OpenSolutionWorkspaceCommand>, OpenSolutionWorkspaceCommandHandler>();
        services.AddSingleton<ICommandHandler<AddProjectReferencesCommand>, AddProjectReferencesCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveProjectReferenceCommand>, RemoveProjectReferenceCommandHandler>();
        services.AddSingleton<ICommandHandler<SetStartupProjectCommand>, SetStartupProjectCommandHandler>();
    }

    public void Initialize(IModuleHost host)
    {
        host.Events.Subscribe<OpenSolutionRequestedEvent>(e => _ = OpenSolutionAsync(host, e.Path));
        host.Events.Subscribe<RefreshSolutionViewRequestedEvent>(_event => { _ = RefreshSolutionViewAsync(host); });
        host.Workspace.Changed += (_, _) => UpdateSidebar(host);
        UpdateSidebar(host);
        host.SetModuleState(Name, ModuleState.Active);
    }

    private static void UpdateSidebar(IModuleHost host)
    {
        if (host.Workspace.Current.Mode == WorkspaceMode.Solution)
        {
            host.ShellRegions.SetContent(
                ShellRegion.Sidebar,
                "SolutionView",
                "Solution",
                host.Services.GetRequiredService<SolutionViewModel>());
            return;
        }

        host.ShellRegions.ClearContent(ShellRegion.Sidebar, "SolutionView");
    }

    private static async Task OpenSolutionAsync(IModuleHost host, string path)
    {
        var handler = host.Services.GetRequiredService<ICommandHandler<OpenSolutionWorkspaceCommand>>();
        await handler.HandleAsync(new OpenSolutionWorkspaceCommand(path));
    }

    private static Task RefreshSolutionViewAsync(IModuleHost host)
    {
        return host.Services.GetRequiredService<SolutionViewModel>().ReloadCurrentSolutionAsync();
    }
}
