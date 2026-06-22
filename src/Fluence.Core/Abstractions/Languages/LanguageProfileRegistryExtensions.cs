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
    /// Detects the most likely language profile for a specific file path using workspace context
    /// when the extension alone is ambiguous.
    /// </summary>
    public static string? DetectLanguageForFile(
        this ILanguageProfileRegistry profiles,
        IWorkspaceContext workspace,
        string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        if (string.Equals(Path.GetExtension(filePath), ".h", System.StringComparison.OrdinalIgnoreCase))
            return DetectLanguageForAmbiguousHeader(profiles, workspace, filePath);

        return profiles.DetectLanguageForFile(filePath);
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

        var fileLanguage = profiles.DetectLanguageForFile(workspace, activeFilePath);
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

    private static string? DetectLanguageForAmbiguousHeader(
        ILanguageProfileRegistry profiles,
        IWorkspaceContext workspace,
        string filePath)
    {
        var siblingLanguage = DetectLanguageFromSiblingSources(workspace, filePath);
        if (siblingLanguage is not null)
            return siblingLanguage;

        var current = workspace.Current;
        var rootPath = current.Mode switch
        {
            WorkspaceMode.Solution when current.CurrentSolutionPath is not null
                => Path.GetDirectoryName(current.CurrentSolutionPath),
            WorkspaceMode.Folder => current.CurrentFolderPath,
            WorkspaceMode.FileOnly => Path.GetDirectoryName(filePath),
            _ => null,
        };

        var workspaceLanguage = rootPath is null ? null : profiles.Detect(rootPath)?.LanguageId;
        if (workspaceLanguage is not null)
            return workspaceLanguage;

        return profiles.GetById("cpp")?.LanguageId ?? profiles.GetById("c")?.LanguageId;
    }

    private static string? DetectLanguageFromSiblingSources(
        IWorkspaceContext workspace,
        string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
            return null;

        var siblings = workspace.Current.TabSession.Documents
            .Where(document =>
                document.Kind == OpenDocumentKind.TextDocument &&
                string.Equals(Path.GetDirectoryName(document.Path), directory, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFileNameWithoutExtension(document.Path), fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase))
            .Select(document => Path.GetExtension(document.Path))
            .ToArray();

        if (siblings.Any(extension => string.Equals(extension, ".c", StringComparison.OrdinalIgnoreCase)))
            return "c";

        if (siblings.Any(extension =>
                string.Equals(extension, ".cpp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".cc", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".cxx", StringComparison.OrdinalIgnoreCase)))
            return "cpp";

        var candidateBasePath = Path.Combine(directory, fileNameWithoutExtension);
        if (File.Exists(candidateBasePath + ".c"))
            return "c";

        if (File.Exists(candidateBasePath + ".cpp") ||
            File.Exists(candidateBasePath + ".cc") ||
            File.Exists(candidateBasePath + ".cxx"))
            return "cpp";

        return null;
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
