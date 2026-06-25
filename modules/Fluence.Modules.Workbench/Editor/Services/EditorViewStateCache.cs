using System.Collections.Generic;

namespace Fluence.Modules.Workbench.Editor.Services;

public sealed class EditorViewStateCache
{
    public List<EditorDocumentViewState> Documents { get; set; } = [];
}
