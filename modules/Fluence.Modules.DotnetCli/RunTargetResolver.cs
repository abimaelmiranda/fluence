using System.IO;
using Fluence.Core.Workspace;

namespace Fluence.Modules.DotnetCli;

public sealed class RunTargetResolver(
    IWorkspaceContext workspace,
    IProjectExecutionTargetResolver projectTargets)
{
    internal RunTarget? Resolve()
    {
        var activeDocument = workspace.Current.TabSession.ActiveDocument;
        var activeFilePath = activeDocument?.Kind == OpenDocumentKind.TextDocument
            ? activeDocument.Path
            : null;

        var projectTarget = projectTargets.ResolveProjectTarget();
        if (projectTarget is not null)
            return CreateProjectTarget(projectTarget.ProjectPath, MapKind(projectTarget.Kind));

        if (DotnetPathHelpers.IsRunnableFile(activeFilePath))
            return CreateFileTarget(activeFilePath!);

        return null;
    }

    private static RunTarget CreateProjectTarget(string projectPath, RunTargetKind kind)
    {
        var workingDirectory = Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory();
        return new RunTarget($"dotnet run --project \"{projectPath}\"", workingDirectory, kind);
    }

    private static RunTarget CreateFileTarget(string filePath)
    {
        var workingDirectory = Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory();
        return new RunTarget($"dotnet run --file \"{filePath}\"", workingDirectory, RunTargetKind.File);
    }

    private static RunTargetKind MapKind(ProjectExecutionTargetKind kind) =>
        kind switch
        {
            ProjectExecutionTargetKind.ActiveFileProject => RunTargetKind.AssociatedProject,
            ProjectExecutionTargetKind.NearestProject => RunTargetKind.NearestProject,
            ProjectExecutionTargetKind.StartupProject => RunTargetKind.StartupProject,
            _ => RunTargetKind.AssociatedProject,
        };
}
