namespace Fluence.Core.Abstractions.File;

public interface IFileViewerRegistry
{
    void Register(string extension);
    bool HasViewer(string filePath);
}
