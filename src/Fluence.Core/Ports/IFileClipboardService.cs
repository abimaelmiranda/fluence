using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Ports;

public interface IFileClipboardService
{
    string? SourcePath { get; }

    bool HasSourcePath { get; }

    void Copy(string path);

    Task<string> PasteAsync(string destinationDirectory, CancellationToken cancellationToken = default);
}
