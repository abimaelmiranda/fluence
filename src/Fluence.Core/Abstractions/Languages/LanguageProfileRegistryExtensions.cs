using System.IO;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Core.Abstractions.Languages;

public static class LanguageProfileRegistryExtensions
{
    /// <summary>
    /// Detects the active language profile ID for the current workspace.
    /// Returns null if the workspace is empty or the language is unrecognized.
    /// </summary>
    public static string? DetectWorkspaceLanguage(
        this ILanguageProfileRegistry profiles,
        IWorkspaceContext workspace)
    {
        var current = workspace.Current;

        var rootPath = current.Mode switch
        {
            WorkspaceMode.Solution when current.CurrentSolutionPath is not null
                => Path.GetDirectoryName(current.CurrentSolutionPath),
            WorkspaceMode.Folder => current.CurrentFolderPath,
            _ => null,
        };

        return rootPath is null ? null : profiles.Detect(rootPath)?.LanguageId;
    }
}
