using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Modules.Editor;

public interface ITextFileService
{
    bool CanOpenAsText(string path);

    Task<string> ReadTextAsync(string path, CancellationToken cancellationToken = default);

    Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default);
}
