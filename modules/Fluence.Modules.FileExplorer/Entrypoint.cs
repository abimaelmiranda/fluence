using System;
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
using Fluence.Modules.FileExplorer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.FileExplorer;

public sealed class Entrypoint : IModule
{
    private string? _activeTabId;
    private IDisposable? _activitySubscription;
    private IDisposable? _openFolderSubscription;
    private IWorkspaceContext? _workspace;
    private EventHandler? _workspaceChanged;

    public string Name => "FileExplorer";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<FileExplorerViewModel>();
        services.AddSingleton<ICommandHandler<OpenFolderWorkspaceCommand>, OpenFolderWorkspaceCommandHandler>();
    }

    public void Initialize(IModuleHost host)
    {
        var scheduler = host.Services.GetRequiredService<ITaskScheduler>();

        _activitySubscription = host.Events.SubscribeSync<ActivityBarTabChangedEvent>(e =>
        {
            _activeTabId = e.TabId;
            UpdateSidebar(host);
        });
        _openFolderSubscription = host.Events.SubscribeSync<OpenFolderRequestedEvent>(e =>
            scheduler.Schedule("workspace.open-folder", TaskPriority.Interactive,
                ct => OpenFolderAsync(host, e.Path, ct),
                correlationId: e.Path));
        _workspace = host.Workspace;
        _workspaceChanged = (_, _) => UpdateSidebar(host);
        host.Workspace.Changed += _workspaceChanged;
        UpdateSidebar(host);
        host.SetModuleState(Name, ModuleState.Active);
    }

    private void UpdateSidebar(IModuleHost host)
    {
        bool shouldShow = _activeTabId == "Files"
                       && host.Workspace.Current.NavigationMode == WorkspaceMode.Folder;

        if (shouldShow)
        {
            host.ShellRegions.SetContent(
                ShellRegion.Sidebar,
                "FileExplorer",
                "Files",
                host.Services.GetRequiredService<FileExplorerViewModel>());
            return;
        }

        host.ShellRegions.ClearContent(ShellRegion.Sidebar, "FileExplorer");
    }

    private static async Task OpenFolderAsync(IModuleHost host, string path, CancellationToken ct)
    {
        var handler = host.Services.GetRequiredService<ICommandHandler<OpenFolderWorkspaceCommand>>();
        await handler.HandleAsync(new OpenFolderWorkspaceCommand(path), ct);
    }

    public ValueTask DisposeAsync()
    {
        _activitySubscription?.Dispose();
        _openFolderSubscription?.Dispose();
        if (_workspace is not null && _workspaceChanged is not null)
            _workspace.Changed -= _workspaceChanged;
        _activitySubscription = null;
        _openFolderSubscription = null;
        _workspace = null;
        _workspaceChanged = null;
        return ValueTask.CompletedTask;
    }
}
