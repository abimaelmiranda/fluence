using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Workspace;

namespace Fluence.Modules.DotnetCli;

public sealed class RunTargetResolver(
    IWorkspaceContext workspace,
    IProjectExecutionTargetResolver projectTargets)
{
    internal async Task<RunTarget?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var activeDocument = workspace.Current.TabSession.ActiveDocument;
        var activeFilePath = activeDocument?.Kind == OpenDocumentKind.TextDocument
            ? activeDocument.Path
            : null;

        var projectTarget = await projectTargets.ResolveProjectTargetAsync(ExecutionMode.Release, cancellationToken);
        if (projectTarget is not null)
            return CreateProjectTarget(projectTarget);

        if (DotnetPathHelpers.IsRunnableFile(activeFilePath))
            return CreateFileTarget(activeFilePath!);

        return null;
    }

    private static RunTarget CreateProjectTarget(ProjectExecutionTarget target)
    {
        var workingDirectory = Path.GetDirectoryName(target.ProjectPath) ?? Directory.GetCurrentDirectory();
        var args = target.Configuration?.Args is { Count: > 0 } configurationArgs
            ? " -- " + string.Join(" ", configurationArgs.Select(QuoteArgument))
            : string.Empty;
        return new RunTarget(
            $"dotnet run --project {QuoteArgument(target.ProjectPath)}{args}",
            workingDirectory,
            MapKind(target.Kind));
    }

    private static RunTarget CreateFileTarget(string filePath)
    {
        var workingDirectory = Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory();
        return new RunTarget($"dotnet run --file {QuoteArgument(filePath)}", workingDirectory, RunTargetKind.File);
    }

    private static RunTargetKind MapKind(ProjectExecutionTargetKind kind) =>
        kind switch
        {
            ProjectExecutionTargetKind.ActiveFileProject => RunTargetKind.AssociatedProject,
            ProjectExecutionTargetKind.NearestProject => RunTargetKind.NearestProject,
            ProjectExecutionTargetKind.StartupProject => RunTargetKind.StartupProject,
            ProjectExecutionTargetKind.LaunchSettings => RunTargetKind.StartupProject,
            _ => RunTargetKind.AssociatedProject,
        };

    private static string QuoteArgument(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
