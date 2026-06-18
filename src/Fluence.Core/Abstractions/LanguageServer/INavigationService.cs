using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Core.Abstractions.LanguageServer;

public interface INavigationService
{
    Task<LspLocation?> GetDefinitionAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    Task<LspLocation?> GetImplementationAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);

    Task<LspLocation?> GetTypeDefinitionAsync(string filePath, int line, int character, CancellationToken cancellationToken = default);
}
