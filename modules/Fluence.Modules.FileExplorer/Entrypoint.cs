using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Services.Modules;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.FileExplorer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.FileExplorer;

public sealed class Entrypoint : IModule
{
    private string? _activeTabId;

    public string Name => "FileExplorer";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<FileExplorerViewModel>();
        services.AddSingleton<ICommandHandler<OpenFolderWorkspaceCommand>, OpenFolderWorkspaceCommandHandler>();
    }

    public void Initialize(IModuleHost host)
    {
        var scheduler = host.Services.GetRequiredService<ITaskScheduler>();

        host.Events.SubscribeSync<ActivityBarTabChangedEvent>(e =>
        {
            _activeTabId = e.TabId;
            UpdateSidebar(host);
        });
        host.Events.SubscribeSync<OpenFolderRequestedEvent>(e =>
            scheduler.Schedule("workspace.open-folder", TaskPriority.Interactive,
                ct => OpenFolderAsync(host, e.Path, ct),
                correlationId: e.Path));
        host.Workspace.Changed += (_, _) => UpdateSidebar(host);
        UpdateSidebar(host);
        host.SetModuleState(Name, ModuleState.Active);
    }

    private void UpdateSidebar(IModuleHost host)
    {
        bool shouldShow = _activeTabId == "Files"
                       && host.Workspace.Current.Mode == WorkspaceMode.Folder;

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
}
