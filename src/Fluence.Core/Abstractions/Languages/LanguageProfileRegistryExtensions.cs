using System.IO;
using System.Linq;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Core.Abstractions.Languages;

public static class LanguageProfileRegistryExtensions
{
    /// <summary>
    /// Detects the most likely language profile for a specific file path.
    /// Returns null when no registered profile claims the file.
    /// </summary>
    public static string? DetectLanguageForFile(
        this ILanguageProfileRegistry profiles,
        string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        return profiles.All
            .FirstOrDefault(profile => profile.CanHandleFile(filePath, string.Empty))
            ?.LanguageId;
    }

    /// <summary>
    /// Detects the active language profile ID for the current workspace.
    /// Returns null if the workspace is empty or the language is unrecognized.
    /// </summary>
    public static string? DetectActiveLanguage(
        this ILanguageProfileRegistry profiles,
        IWorkspaceContext workspace)
    {
        var current = workspace.Current;

        var activeFilePath = current.TabSession.ActiveDocument?.Kind == OpenDocumentKind.TextDocument
            ? current.TabSession.ActiveDocument.Path
            : current.CurrentFilePath;

        var fileLanguage = profiles.DetectLanguageForFile(activeFilePath);
        if (fileLanguage is not null)
            return fileLanguage;

        var rootPath = current.Mode switch
        {
            WorkspaceMode.Solution when current.CurrentSolutionPath is not null
                => Path.GetDirectoryName(current.CurrentSolutionPath),
            WorkspaceMode.Folder => current.CurrentFolderPath,
            WorkspaceMode.FileOnly => Path.GetDirectoryName(activeFilePath),
            _ => null,
        };

        return rootPath is null ? null : profiles.Detect(rootPath)?.LanguageId;
    }

    /// <summary>
    /// Detects the active language profile ID for the current workspace.
    /// Returns null if the workspace is empty or the language is unrecognized.
    /// </summary>
    public static string? DetectWorkspaceLanguage(
        this ILanguageProfileRegistry profiles,
        IWorkspaceContext workspace)
        => profiles.DetectActiveLanguage(workspace);
}
