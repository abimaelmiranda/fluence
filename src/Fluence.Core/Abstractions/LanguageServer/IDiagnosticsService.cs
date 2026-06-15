using System.Collections.Generic;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Core.Abstractions.LanguageServer;

public interface IDiagnosticsService
{
    IReadOnlyList<LspDiagnostic> GetDiagnostics(string filePath);

    void UpdateDiagnostics(string filePath, IReadOnlyList<LspDiagnostic> diagnostics);
}
