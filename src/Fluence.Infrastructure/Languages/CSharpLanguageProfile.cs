using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fluence.Core.Abstractions.Languages;

namespace Fluence.Infrastructure.Languages;

public sealed class CSharpLanguageProfile : ILanguageProfile
{
    public string LanguageId => "csharp";

    public string DisplayName => "C#";

    public IReadOnlyList<string> WorkspaceFilePatterns { get; } = ["*.sln", "*.slnx", "*.csproj"];

    public IReadOnlyList<string> FileExtensions { get; } = [".cs", ".csx"];

    public bool MatchesWorkspace(string folderPath)
    {
        foreach (var pattern in WorkspaceFilePatterns)
        {
            if (Directory.EnumerateFiles(folderPath, pattern, SearchOption.AllDirectories)
                         .GetEnumerator().MoveNext())
                return true;
        }
        return false;
    }

    public bool CanHandleFile(string filePath, string languageId) =>
        string.Equals(languageId, LanguageId, StringComparison.OrdinalIgnoreCase) ||
        FileExtensions.Contains(Path.GetExtension(filePath),
            StringComparer.OrdinalIgnoreCase);
}
