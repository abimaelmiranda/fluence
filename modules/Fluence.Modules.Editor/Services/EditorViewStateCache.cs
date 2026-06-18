using System.Collections.Generic;

namespace Fluence.Modules.Editor.Services;

public sealed class EditorViewStateCache
{
    public List<EditorDocumentViewState> Documents { get; set; } = [];
}
