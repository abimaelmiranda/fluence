using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;

namespace Fluence.Modules.FileExplorer;

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
