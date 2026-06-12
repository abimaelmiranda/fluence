namespace Fluence.Core.Ports;

public interface IFileService
{
    void Delete(string path, bool isDirectory);
}
