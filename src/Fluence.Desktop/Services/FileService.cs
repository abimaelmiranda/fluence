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

    public void CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public void WriteText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content);
    }
}
