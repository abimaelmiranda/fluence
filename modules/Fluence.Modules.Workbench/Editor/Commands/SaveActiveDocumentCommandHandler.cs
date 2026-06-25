using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.Workbench.Editor.Abstractions;

namespace Fluence.Modules.Workbench.Editor.Commands;

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