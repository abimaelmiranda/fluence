using System;
using System.Collections.Generic;
using System.Linq;
using Fluence.Modules.LanguageServer;

namespace Fluence.Modules.LanguageServer.Services;

internal static class SuppressedDiagnosticCodes
{
    public static HashSet<string> From(LanguageServerSettings settings) =>
        settings.SuppressedDiagnosticCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool Contains(IReadOnlySet<string> codes, string? code) =>
        !string.IsNullOrWhiteSpace(code) && codes.Contains(code.Trim());
}
