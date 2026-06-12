using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Modules;
using Fluence.Core.Workspace;
using Fluence.Modules.SolutionView.ViewModels;
using Fluence.Modules.SolutionView.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.SolutionView;

public sealed class SolutionViewModule : IIdeModule
{
    public string Name => "SolutionView";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<SolutionViewModel>();
        services.AddSingleton<ISolutionWorkspaceLoader, BuildalyzerSolutionWorkspaceLoader>();
        services.AddSingleton<IProjectReferenceService, ProjectReferenceService>();
        services.AddSingleton<IProjectReferenceDialogService, AvaloniaProjectReferenceDialogService>();
        services.AddSingleton<ICommandHandler<OpenSolutionWorkspaceCommand>, OpenSolutionWorkspaceCommandHandler>();
        services.AddSingleton<ICommandHandler<AddProjectReferencesCommand>, AddProjectReferencesCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveProjectReferenceCommand>, RemoveProjectReferenceCommandHandler>();
        services.AddSingleton<ICommandHandler<SetStartupProjectCommand>, SetStartupProjectCommandHandler>();
    }

    public void RegisterViews(IViewRegistry registry)
    {
        registry.Register<SolutionViewModel, Views.SolutionView>();
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
