using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Workspace;
using Fluence.Modules.Editor.Abstractions;
using Fluence.Modules.Editor.Exceptions;

namespace Fluence.Modules.Editor.Commands;

public sealed class OpenFileWorkspaceCommandHandler(IWorkspaceContext workspace, ITextFileService textFiles)
    : ICommandHandler<OpenFileWorkspaceCommand>
{
    public async Task HandleAsync(OpenFileWorkspaceCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!textFiles.CanOpenAsText(command.Path))
        {
            throw new UnsupportedTextFileException(command.Path);
        }

        var content = await textFiles.ReadTextAsync(command.Path, cancellationToken);
        workspace.OpenFile(command.Path, content);
    }
}