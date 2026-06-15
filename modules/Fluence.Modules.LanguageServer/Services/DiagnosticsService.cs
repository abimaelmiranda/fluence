using System.Collections.Concurrent;
using System.Collections.Generic;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Models.LanguageServer;

namespace Fluence.Modules.LanguageServer.Services;

internal sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<LspDiagnostic>> _store =
        new(System.StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<LspDiagnostic> GetDiagnostics(string filePath) =>
        _store.TryGetValue(filePath, out var list) ? list : [];

    public void UpdateDiagnostics(string filePath, IReadOnlyList<LspDiagnostic> diagnostics) =>
        _store[filePath] = diagnostics;
}
