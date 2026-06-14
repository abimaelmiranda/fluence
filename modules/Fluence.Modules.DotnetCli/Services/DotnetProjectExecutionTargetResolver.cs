using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Workspace;

namespace Fluence.Modules.DotnetCli.Services;

public sealed class DotnetProjectExecutionTargetResolver(
    IWorkspaceContext workspace,
    IProjectAssociationService projectAssociations,
    ILaunchSettingsService launchSettingsService,
    ILaunchSettingsCoordinator launchSettings)
    : IProjectExecutionTargetResolver
{
    public async Task<ProjectExecutionTarget?> ResolveProjectTargetAsync(
        ExecutionMode mode,
        CancellationToken cancellationToken = default)
    {
        var configuredTarget = await ResolveConfiguredProjectAsync(mode, cancellationToken);
        if (configuredTarget is not null)
            return configuredTarget;
        if (mode == ExecutionMode.Debug)
            return null;

        var activeDocument = workspace.Current.TabSession.ActiveDocument;
        var activeFilePath = activeDocument?.Kind == OpenDocumentKind.TextDocument
            ? activeDocument.Path
            : null;

        var activeProject = ResolveActiveProject(activeFilePath);
        if (activeProject.ProjectPath is not null && DotnetProjectRunCapability.CanRun(activeProject.ProjectPath))
            return new ProjectExecutionTarget(activeProject.ProjectPath, activeProject.Kind);

        var startupProjectPath = workspace.Current.Mode is WorkspaceMode.Solution or WorkspaceMode.Debugging
            ? workspace.Current.StartupProjectPath
            : null;
        return DotnetProjectRunCapability.CanRun(startupProjectPath)
            ? new ProjectExecutionTarget(startupProjectPath!, ProjectExecutionTargetKind.StartupProject)
            : null;
    }

    private async Task<ProjectExecutionTarget?> ResolveConfiguredProjectAsync(
        ExecutionMode mode,
        CancellationToken cancellationToken)
    {
        var settings = mode == ExecutionMode.Debug
            ? await launchSettings.EnsureAsync(cancellationToken)
            : await launchSettings.LoadExistingAsync(cancellationToken);
        if (settings is null || string.IsNullOrWhiteSpace(settings.StartupProject))
            return null;

        var workspaceRoot = ResolveWorkspaceRoot();
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            return null;

        var projectPath = Path.IsPathRooted(settings.StartupProject)
            ? settings.StartupProject
            : Path.GetFullPath(Path.Combine(workspaceRoot, settings.StartupProject));

        if (!DotnetProjectRunCapability.CanRun(projectPath))
            return null;

        workspace.SetStartupProject(projectPath);
        return new ProjectExecutionTarget(projectPath, ProjectExecutionTargetKind.LaunchSettings, settings.DefaultConfiguration);
    }

    private string? ResolveWorkspaceRoot()
    {
        return launchSettingsService.GetWorkspaceRoot(workspace.Current);
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
