using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Core.Abstractions.LanguageServer;

public interface ISignatureHelpService
{
    Task<LspSignatureHelp?> GetSignatureHelpAsync(
        string filePath,
        int line,
        int character,
        bool isRetrigger = false,
        CancellationToken cancellationToken = default);
}
