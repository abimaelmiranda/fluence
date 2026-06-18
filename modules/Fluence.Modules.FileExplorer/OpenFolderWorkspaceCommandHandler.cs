using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Modules.FileExplorer;

public sealed class OpenFolderWorkspaceCommandHandler(
    IWorkspaceContext workspace,
    IRecentProjectsService recentProjects,
    IWorkspaceSnapshotService snapshots)
    : ICommandHandler<OpenFolderWorkspaceCommand>
{
    public async Task HandleAsync(OpenFolderWorkspaceCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        workspace.OpenFolder(command.Path);

        await recentProjects.AddAsync(command.Path, RecentProjectKind.Folder, cancellationToken);

        var snapshot = await snapshots.LoadAsync(command.Path, cancellationToken);
        if (snapshot is null) return;

        foreach (var tabPath in snapshot.OpenTabs)
        {
            if (!File.Exists(tabPath)) continue;
            try
            {
                var content = await File.ReadAllTextAsync(tabPath, cancellationToken);
                workspace.OpenFile(tabPath, content);
            }
            catch
            {
                // silently skip unreadable files
            }
        }

        if (snapshot.ActiveTabPath is not null)
            workspace.ActivateDocument(snapshot.ActiveTabPath);
    }
}
