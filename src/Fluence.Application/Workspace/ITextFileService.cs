using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Application.Workspace;

public interface ITextFileService
{
    bool CanOpenAsText(string path);

    Task<string> ReadTextAsync(string path, CancellationToken cancellationToken = default);

    Task WriteTextAsync(string path, string content, CancellationToken cancellationToken = default);
}
