using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Core.Abstractions.LanguageServer;

public interface ICompletionService
{
    Task<IReadOnlyList<LspCompletion>> GetCompletionsAsync(
        string filePath,
        int line,
        int character,
        CancellationToken cancellationToken = default);
}
