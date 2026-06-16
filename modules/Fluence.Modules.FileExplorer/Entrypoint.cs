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
        host.Events.SubscribeSync<ActivityBarTabChangedEvent>(e =>
        {
            _activeTabId = e.TabId;
            UpdateSidebar(host);
        });
        host.Events.SubscribeSync<OpenFolderRequestedEvent>(e => { _ = OpenFolderAsync(host, e.Path); });
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

    private static async Task OpenFolderAsync(IModuleHost host, string path)
    {
        var handler = host.Services.GetRequiredService<ICommandHandler<OpenFolderWorkspaceCommand>>();
        await handler.HandleAsync(new OpenFolderWorkspaceCommand(path));
    }
}
