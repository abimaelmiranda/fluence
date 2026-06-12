using System.IO;
using Fluence.Core.Ports;

namespace Fluence.Desktop.Services;

public sealed class FileService : IFileService
{
    public void Delete(string path, bool isDirectory)
    {
        if (isDirectory)
            Directory.Delete(path, recursive: true);
        else
            File.Delete(path);
    }
}
