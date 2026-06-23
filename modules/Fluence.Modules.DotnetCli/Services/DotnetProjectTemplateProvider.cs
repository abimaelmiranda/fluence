using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Projects;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Models.Output;
using Fluence.Modules.DotnetCli.Services;

namespace Fluence.Modules.DotnetCli.Services;

public sealed class DotnetProjectTemplateProvider(
    IDotnetSdkProvisioningService sdk,
    IProcessHost processHost,
    IOutputChannelService output,
    ILocalizationService loc) : IProjectTemplateProvider
{
    public string ProviderId => "csharp";

    public string DisplayName => ".NET";

    public bool SupportsSolutionCreation => true;

    public IReadOnlyList<ProjectTemplateDefinition> Templates { get; } =
    [
        new("console", "Console App", "An empty console application", SupportsFramework: true),
        new("classlib", "Class Library", "A reusable library project", SupportsFramework: true),
        new("worker", "Worker Service", "A background service host", SupportsFramework: true),
        new("webapi", "Web API", "An ASP.NET Core API project", SupportsFramework: true),
        new("mvc", "MVC", "An ASP.NET Core MVC app", SupportsFramework: true),
        new("webapp", "Razor Pages", "A Razor Pages app", SupportsFramework: true),
        new("blazor", "Blazor Web App", "A Blazor web application", SupportsFramework: true),
        new("wpf", "WPF App", "A desktop application using WPF"),
        new("xunit", "xUnit Test Project", "A test project using xUnit", SupportsFramework: true),
        new("nunit", "NUnit Test Project", "A test project using NUnit", SupportsFramework: true),
        new("mstest", "MSTest Test Project", "A test project using MSTest", SupportsFramework: true),
    ];

    public bool CanHandleWorkspace(string? languageId) =>
        languageId is null or "csharp";

    public async Task<ProjectCreationResult> CreateAsync(
        ProjectCreationRequest request,
        Action<string> onOutput,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var sdkStatus = await sdk.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!sdkStatus.IsDotnetAvailable || sdkStatus.InstalledSdks.Count == 0)
            throw new InvalidOperationException(loc.Get("DotnetCli.Status.SdkRequiredCreate"));

        onOutput($"[dotnet] Creating {request.ProjectName} with {request.Template.Name}");
        Directory.CreateDirectory(request.Location);
        var projectRoot = Path.Combine(request.Location, request.ProjectName);

        var template = request.Template;
        var arguments = new List<string> { "new", template.Id, "-n", request.ProjectName, "-o", projectRoot };
        if (template.SupportsFramework && !string.IsNullOrWhiteSpace(request.Framework))
        {
            arguments.Add("-f");
            arguments.Add(request.Framework);
        }

        await RunDotnetAsync(arguments, request.Location, cancellationToken).ConfigureAwait(false);

        string? solutionPath = null;
        if (request.CreateSolution)
        {
            var solutionName = string.IsNullOrWhiteSpace(request.SolutionName) ? request.ProjectName : request.SolutionName.Trim();
            var solutionLocation = request.PlaceSolutionInProjectFolder ? projectRoot : request.Location;
            onOutput($"[dotnet] Creating solution {solutionName}");
            await RunDotnetAsync(
                ["new", "sln", "-n", solutionName, "-o", solutionLocation],
                solutionLocation,
                cancellationToken).ConfigureAwait(false);
            solutionPath = ResolveSolutionFile(solutionLocation, solutionName);
            if (solutionPath is null)
                throw new FileNotFoundException(string.Format(loc.Get("DotnetCli.Error.SolutionFileNotCreated"), solutionLocation));
            onOutput($"[dotnet] Adding project to {solutionPath}");
            await RunDotnetAsync(
                ["sln", solutionPath, "add", FindProjectFile(projectRoot) ?? projectRoot],
                solutionLocation,
                cancellationToken).ConfigureAwait(false);
        }

        onOutput($"[dotnet] Project created at {projectRoot}");
        return new ProjectCreationResult(projectRoot, solutionPath);
    }

    private async Task RunDotnetAsync(IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var dotnet = await sdk.ResolveDotnetExecutableAsync(cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(OutputChannelIds.Run, $"> {DotnetCommandLine.Format(dotnet, arguments)}{Environment.NewLine}", cancellationToken: cancellationToken);

        await processHost.RunAsync(
            dotnet,
            arguments,
            workingDirectory,
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine),
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine, OutputLogLevel.Error),
            cancellationToken);
    }

    private static string? FindProjectFile(string projectRoot) =>
        Directory.Exists(projectRoot)
            ? Directory.EnumerateFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault()
            : null;

    private static string? ResolveSolutionFile(string location, string solutionName)
    {
        var candidates = new[]
        {
            Path.Combine(location, $"{solutionName}.slnx"),
            Path.Combine(location, $"{solutionName}.sln"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
}
