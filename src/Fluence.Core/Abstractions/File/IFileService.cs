namespace Fluence.Core.Abstractions.File;

public interface IFileService
{
    void Delete(string path, bool isDirectory);

    void CreateDirectory(string path);

    void WriteText(string path, string content);
}
