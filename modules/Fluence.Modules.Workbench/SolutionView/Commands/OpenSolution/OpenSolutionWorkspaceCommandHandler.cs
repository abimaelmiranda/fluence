using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Modules.Workbench.SolutionView.Commands.OpenSolution;

public sealed class OpenSolutionWorkspaceCommandHandler(
    IWorkspaceContext workspace,
    IRecentProjectsService recentProjects,
    IWorkspaceSnapshotService snapshots)
    : ICommandHandler<OpenSolutionWorkspaceCommand>
{
    public async Task HandleAsync(OpenSolutionWorkspaceCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        workspace.OpenSolution(command.Path);

        await recentProjects.AddAsync(command.Path, RecentProjectKind.Solution, cancellationToken);

        var workspaceRoot = Path.GetDirectoryName(command.Path);
        if (workspaceRoot is null) return;

        var snapshot = await snapshots.LoadAsync(workspaceRoot, cancellationToken);
        if (snapshot is null) return;

        foreach (var tabPath in snapshot.OpenTabs.Reverse())
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
