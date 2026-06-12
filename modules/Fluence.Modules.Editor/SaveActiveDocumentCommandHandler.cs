using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Workspace;

namespace Fluence.Modules.Editor;

public sealed class SaveActiveDocumentCommandHandler(IWorkspaceContext workspace, ITextFileService textFiles)
    : ICommandHandler<SaveActiveDocumentCommand>
{
    public async Task HandleAsync(SaveActiveDocumentCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var document = workspace.Current.TabSession.ActiveDocument;
        if (document is null || document.Kind != OpenDocumentKind.TextDocument)
        {
            return;
        }

        await textFiles.WriteTextAsync(document.Path, document.Content, cancellationToken);
        workspace.MarkActiveDocumentSaved();
    }
}
