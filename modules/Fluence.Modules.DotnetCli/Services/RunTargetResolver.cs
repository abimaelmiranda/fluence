using System;
using System.Collections.Generic;
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
    ILaunchSettingsService launchSettings,
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
        var profile = await ResolveRunProfileAsync(target, cancellationToken);
        var arguments = new List<string> { "run", "--project", target.ProjectPath };
        if (profile is not null)
        {
            arguments.Add("--launch-profile");
            arguments.Add(profile.Name);
        }

        if (target.Configuration?.Args is { Count: > 0 } configurationArgs)
        {
            arguments.Add("--");
            arguments.AddRange(configurationArgs);
        }

        return new RunTarget(
            dotnet,
            arguments,
            workingDirectory,
            CreateEnvironment(profile),
            MapKind(target.Kind));
    }

    private async Task<RunTarget> CreateFileTargetAsync(string filePath, CancellationToken cancellationToken)
    {
        var dotnet = await sdk.ResolveDotnetExecutableAsync(cancellationToken);
        var workingDirectory = Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory();
        return new RunTarget(dotnet, ["run", "--file", filePath], workingDirectory, null, RunTargetKind.File);
    }

    private async Task<DotnetLaunchProfile?> ResolveRunProfileAsync(
        ProjectExecutionTarget target,
        CancellationToken cancellationToken)
    {
        var profileName = target.Configuration?.RunProfileName;
        if (string.IsNullOrWhiteSpace(profileName))
            return null;

        var profiles = await launchSettings.DiscoverLaunchProfilesAsync(target.ProjectPath, cancellationToken);
        return profiles.FirstOrDefault(profile =>
            string.Equals(profile.Name, profileName, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyDictionary<string, string>? CreateEnvironment(DotnetLaunchProfile? profile)
    {
        if (profile is null)
            return null;

        var env = new Dictionary<string, string>(profile.EnvironmentVariables, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(profile.ApplicationUrl) &&
            !env.ContainsKey("ASPNETCORE_URLS"))
            env["ASPNETCORE_URLS"] = profile.ApplicationUrl;

        return env.Count == 0 ? null : env;
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

}
