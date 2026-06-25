namespace Fluence.Modules.Workbench.Editor.Services;

public sealed class EditorDocumentViewState
{
    public string FilePath { get; set; } = string.Empty;
    public int CaretOffset { get; set; }
    public double ScrollX { get; set; }
    public double ScrollY { get; set; }
}
