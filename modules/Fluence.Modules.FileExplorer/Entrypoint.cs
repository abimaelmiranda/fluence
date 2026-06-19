using System;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.FileExplorer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.FileExplorer;

public sealed class Entrypoint : IModule
{
    private IDisposable? _openFolderSubscription;

    public string Id => "FileExplorer";

    public string DisplayName => "File Explorer";

    public int StartupOrder => 200;

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<FileExplorerViewModel>();
        services.AddSingleton<ICommandHandler<OpenFolderWorkspaceCommand>, OpenFolderWorkspaceCommandHandler>();
    }

    public ModuleContributions GetContributions() =>
        new()
        {
            Panels =
            [
                new ShellPanelContribution(
                    ShellRegion.Sidebar,
                    Id,
                    "Files",
                    services => services.GetRequiredService<FileExplorerViewModel>(),
                    PanelVisibilityRule.Custom((workspace, activeTabId, _) =>
                        activeTabId == "Files" &&
                        workspace.NavigationMode == WorkspaceMode.Folder)),
            ],
        };

    public Task InitializeAsync(IModuleHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scheduler = host.Services.GetRequiredService<ITaskScheduler>();

        _openFolderSubscription = host.Events.SubscribeSync<OpenFolderRequestedEvent>(e =>
            scheduler.Schedule("workspace.open-folder", TaskPriority.Interactive,
                ct => OpenFolderAsync(host, e.Path, ct),
                correlationId: e.Path));
        host.SetModuleState(Id, ModuleState.Active);
        return Task.CompletedTask;
    }

    private static async Task OpenFolderAsync(IModuleHost host, string path, CancellationToken ct)
    {
        var handler = host.Services.GetRequiredService<ICommandHandler<OpenFolderWorkspaceCommand>>();
        await handler.HandleAsync(new OpenFolderWorkspaceCommand(path), ct);
    }

    public ValueTask DisposeAsync()
    {
        _openFolderSubscription?.Dispose();
        _openFolderSubscription = null;
        return ValueTask.CompletedTask;
    }
}
