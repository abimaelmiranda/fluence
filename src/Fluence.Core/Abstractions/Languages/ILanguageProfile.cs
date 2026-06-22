using System.Collections.Generic;

namespace Fluence.Core.Abstractions.Languages;

public interface ILanguageProfile
{
    /// <summary>Unique identifier, e.g. "csharp" or "cpp".</summary>
    string LanguageId { get; }

    string DisplayName { get; }

    /// <summary>
    /// Glob patterns checked against workspace root to identify the language.
    /// Matched in order; first hit wins.
    /// </summary>
    IReadOnlyList<string> WorkspaceFilePatterns { get; }

    /// <summary>File extensions owned by this language, including the leading dot.</summary>
    IReadOnlyList<string> FileExtensions { get; }

    bool MatchesWorkspace(string folderPath);

    bool CanHandleFile(string filePath, string languageId);
}
