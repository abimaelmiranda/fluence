using System.Collections.Generic;

namespace Fluence.Core.Models.Theming;

public sealed class IdeTheme
{
    public required string Name { get; init; }

    public required ThemeFont Font { get; init; }

    public required ThemeColors Colors { get; init; }

    public required IReadOnlyDictionary<string, string> SemanticTokenColors { get; init; }

    public required string TextMateThemeJson { get; init; }
}
