using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.DotnetCli.Models;

namespace Fluence.Modules.DotnetCli.Services;

public sealed class RunTargetResolver(
    IWorkspaceContext workspace,
    IProjectExecutionTargetResolver projectTargets,
    IDotnetSdkProvisioningService sdk)
{
    internal async Task<RunTarget?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var activeDocument = workspace.Current.TabSession.ActiveDocument;
        var activeFilePath = activeDocument?.Kind == OpenDocumentKind.TextDocument
            ? activeDocument.Path
            : null;

        var projectTarget = await projectTargets.ResolveProjectTargetAsync(ExecutionMode.Release, cancellationToken);
        if (projectTarget is not null)
            return await CreateProjectTargetAsync(projectTarget, cancellationToken);

        if (DotnetPathHelpers.IsRunnableFile(activeFilePath))
            return await CreateFileTargetAsync(activeFilePath!, cancellationToken);

        return null;
    }

    private async Task<RunTarget> CreateProjectTargetAsync(
        ProjectExecutionTarget target,
        CancellationToken cancellationToken)
    {
        var dotnet = await sdk.ResolveDotnetExecutableAsync(cancellationToken);
        var workingDirectory = Path.GetDirectoryName(target.ProjectPath) ?? Directory.GetCurrentDirectory();
        var args = target.Configuration?.Args is { Count: > 0 } configurationArgs
            ? " -- " + string.Join(" ", configurationArgs.Select(QuoteArgument))
            : string.Empty;
        return new RunTarget(
            dotnet,
            $"run --project {QuoteArgument(target.ProjectPath)}{args}",
            workingDirectory,
            MapKind(target.Kind));
    }

    private async Task<RunTarget> CreateFileTargetAsync(string filePath, CancellationToken cancellationToken)
    {
        var dotnet = await sdk.ResolveDotnetExecutableAsync(cancellationToken);
        var workingDirectory = Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory();
        return new RunTarget(dotnet, $"run --file {QuoteArgument(filePath)}", workingDirectory, RunTargetKind.File);
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
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
