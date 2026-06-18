using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Services.Modules;
using Fluence.Core.Abstractions.Workspace;
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
    private string? _activeTabId;
    private readonly List<IDisposable> _subscriptions = [];
    private IWorkspaceContext? _workspace;
    private EventHandler? _workspaceChanged;

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
        _subscriptions.Add(host.Events.SubscribeSync<ActivityBarTabChangedEvent>(e =>
        {
            _activeTabId = e.TabId;
            UpdateSidebar(host);
        }));
        var scheduler = host.Services.GetRequiredService<ITaskScheduler>();

        _subscriptions.Add(host.Events.SubscribeSync<OpenSolutionRequestedEvent>(e =>
            scheduler.Schedule("workspace.open-solution", TaskPriority.Interactive,
                ct => OpenSolutionAsync(host, e.Path, ct),
                correlationId: e.Path)));
        _subscriptions.Add(host.Events.SubscribeSync<RefreshSolutionViewRequestedEvent>(_ =>
            scheduler.Schedule("solution.refresh", TaskPriority.Maintenance,
                ct => RefreshSolutionViewAsync(host, ct),
                correlationId: "solution.refresh")));
        _subscriptions.Add(host.Events.SubscribeSync<GitCheckoutCompletedEvent>(_ =>
            scheduler.Schedule("solution.refresh", TaskPriority.Maintenance,
                ct => RefreshSolutionViewAsync(host, ct),
                correlationId: "solution.refresh")));
        _workspace = host.Workspace;
        _workspaceChanged = (_, _) => UpdateSidebar(host);
        host.Workspace.Changed += _workspaceChanged;
        UpdateSidebar(host);
        host.SetModuleState(Name, ModuleState.Active);
    }

    private void UpdateSidebar(IModuleHost host)
    {
        bool shouldShow = _activeTabId == "Files"
                       && host.Workspace.Current.NavigationMode == WorkspaceMode.Solution;

        if (shouldShow)
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

    private static async Task OpenSolutionAsync(IModuleHost host, string path, CancellationToken ct)
    {
        var handler = host.Services.GetRequiredService<ICommandHandler<OpenSolutionWorkspaceCommand>>();
        await handler.HandleAsync(new OpenSolutionWorkspaceCommand(path), ct);
    }

    private static Task RefreshSolutionViewAsync(IModuleHost host, CancellationToken ct)
    {
        return host.Services.GetRequiredService<SolutionViewModel>().ReloadCurrentSolutionAsync();
    }

    public ValueTask DisposeAsync()
    {
        foreach (var subscription in _subscriptions)
            subscription.Dispose();
        _subscriptions.Clear();
        if (_workspace is not null && _workspaceChanged is not null)
            _workspace.Changed -= _workspaceChanged;
        _workspace = null;
        _workspaceChanged = null;
        return ValueTask.CompletedTask;
    }
}
