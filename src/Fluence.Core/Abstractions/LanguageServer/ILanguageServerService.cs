using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Abstractions.LanguageServer;

public interface ILanguageServerService
{
    bool IsRunning { get; }

    Task StartAsync(string rootPath, CancellationToken cancellationToken = default);

    Task StopAsync();

    Task SendDidOpenAsync(string filePath, string languageId, string content, CancellationToken cancellationToken = default);

    Task SendDidChangeAsync(string filePath, int version, string content, CancellationToken cancellationToken = default);

    Task SendDidCloseAsync(string filePath, CancellationToken cancellationToken = default);
}
