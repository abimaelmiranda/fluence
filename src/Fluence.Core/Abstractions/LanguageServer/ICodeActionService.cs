using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Core.Abstractions.LanguageServer;

public interface ICodeActionService
{
    Task<LspCodeAction[]> GetCodeActionsAsync(
        string filePath,
        int startLine, int startChar,
        int endLine, int endChar,
        LspDiagnostic? diagnostic,
        CancellationToken cancellationToken = default);

    Task ExecuteCommandAsync(
        string command,
        string? argumentsJson,
        CancellationToken cancellationToken = default);

    Task<LspCodeAction?> ResolveAsync(
        LspCodeAction action,
        CancellationToken cancellationToken = default);
}
