using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Modules.Abstractions;
using Fluence.Core.Modules;
using Fluence.Core.Workspace;
using Fluence.Modules.FileExplorer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.FileExplorer;

public sealed class Entrypoint : IModule
{
    public string Name => "FileExplorer";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<FileExplorerViewModel>();
        services.AddSingleton<ICommandHandler<OpenFolderWorkspaceCommand>, OpenFolderWorkspaceCommandHandler>();
    }

    public void Initialize(IModuleHost host)
    {
        host.Events.Subscribe<OpenFolderRequestedEvent>(e => _ = OpenFolderAsync(host, e.Path));
        host.Workspace.Changed += (_, _) => UpdateSidebar(host);
        UpdateSidebar(host);
        host.SetModuleState(Name, ModuleState.Active);
    }

    private static void UpdateSidebar(IModuleHost host)
    {
        if (host.Workspace.Current.Mode == WorkspaceMode.Folder)
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