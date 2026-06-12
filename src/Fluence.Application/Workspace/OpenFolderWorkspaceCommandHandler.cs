using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Workspace;

namespace Fluence.Application.Workspace;

public sealed class OpenFolderWorkspaceCommandHandler(IWorkspaceContext workspace)
    : ICommandHandler<OpenFolderWorkspaceCommand>
{
    public Task HandleAsync(OpenFolderWorkspaceCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        workspace.OpenFolder(command.Path);
        return Task.CompletedTask;
    }
}
