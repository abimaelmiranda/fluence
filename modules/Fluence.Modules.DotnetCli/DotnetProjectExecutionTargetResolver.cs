using System;
using System.IO;
using System.Linq;
using Fluence.Core.Workspace;

namespace Fluence.Modules.DotnetCli;

public sealed class DotnetProjectExecutionTargetResolver(
    IWorkspaceContext workspace,
    IProjectAssociationService projectAssociations)
    : IProjectExecutionTargetResolver
{
    public ProjectExecutionTarget? ResolveProjectTarget()
    {
        var activeDocument = workspace.Current.TabSession.ActiveDocument;
        var activeFilePath = activeDocument?.Kind == OpenDocumentKind.TextDocument
            ? activeDocument.Path
            : null;

        var activeProject = ResolveActiveProject(activeFilePath);
        if (activeProject.ProjectPath is not null && DotnetProjectRunCapability.CanRun(activeProject.ProjectPath))
            return new ProjectExecutionTarget(activeProject.ProjectPath, activeProject.Kind);

        var startupProjectPath = workspace.Current.Mode == WorkspaceMode.Solution
            ? workspace.Current.StartupProjectPath
            : null;
        return DotnetProjectRunCapability.CanRun(startupProjectPath)
            ? new ProjectExecutionTarget(startupProjectPath!, ProjectExecutionTargetKind.StartupProject)
            : null;
    }

    private ProjectResolution ResolveActiveProject(string? activeFilePath)
    {
        if (string.IsNullOrWhiteSpace(activeFilePath))
            return ProjectResolution.None;

        if (DotnetPathHelpers.IsProjectPath(activeFilePath))
            return new ProjectResolution(activeFilePath, ProjectExecutionTargetKind.ActiveFileProject);

        var solutionPath = workspace.Current.CurrentSolutionPath;
        if (!string.IsNullOrWhiteSpace(solutionPath))
        {
            var associatedProjectPath = projectAssociations.FindProjectForFile(solutionPath, activeFilePath);
            if (!string.IsNullOrWhiteSpace(associatedProjectPath))
                return new ProjectResolution(associatedProjectPath, ProjectExecutionTargetKind.ActiveFileProject);
        }

        var nearestProjectPath = FindNearestProjectForFile(
            activeFilePath,
            ResolveProjectSearchRoot(activeFilePath, solutionPath));
        return string.IsNullOrWhiteSpace(nearestProjectPath)
            ? ProjectResolution.None
            : new ProjectResolution(nearestProjectPath, ProjectExecutionTargetKind.NearestProject);
    }

    private string? ResolveProjectSearchRoot(string filePath, string? solutionPath)
    {
        if (!string.IsNullOrWhiteSpace(solutionPath))
            return Path.GetDirectoryName(solutionPath);

        if (!string.IsNullOrWhiteSpace(workspace.Current.CurrentFolderPath))
            return workspace.Current.CurrentFolderPath;

        return Path.GetPathRoot(filePath);
    }

    private static string? FindNearestProjectForFile(string filePath, string? searchRoot)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(searchRoot) || string.IsNullOrWhiteSpace(directory))
            return null;

        var root = DotnetPathHelpers.NormalizeDirectory(searchRoot);

        while (!string.IsNullOrWhiteSpace(directory))
        {
            var currentDirectory = DotnetPathHelpers.NormalizeDirectory(directory);
            if (!DotnetPathHelpers.IsSameOrChildDirectory(currentDirectory, root))
                return null;

            var projectPath = Directory.EnumerateFiles(currentDirectory, "*.csproj", SearchOption.TopDirectoryOnly)
                .Where(DotnetPathHelpers.IsProjectPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (projectPath is not null)
                return projectPath;

            if (string.Equals(currentDirectory, root, StringComparison.OrdinalIgnoreCase))
                return null;

            directory = Directory.GetParent(currentDirectory)?.FullName;
        }

        return null;
    }

    private sealed record ProjectResolution(string? ProjectPath, ProjectExecutionTargetKind Kind)
    {
        public static ProjectResolution None { get; } = new(null, ProjectExecutionTargetKind.ActiveFileProject);
    }
}
