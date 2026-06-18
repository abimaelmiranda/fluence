using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Core.Abstractions.LanguageServer;

public interface IFormattingService
{
    Task<IReadOnlyList<LspTextEdit>> FormatDocumentAsync(
        string filePath,
        string content,
        int version,
        CancellationToken cancellationToken = default);
}
