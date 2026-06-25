using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Jobs;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Problems;
using Fluence.Core.Abstractions.Projects;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Abstractions.Storage;
using Fluence.Core.Abstractions.Toolchains;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Events.Toolchains;
using Fluence.Core.Events.Ui;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Models.Jobs;
using Fluence.Core.Models.Output;
using Fluence.Core.Models.Problems;
using Fluence.Core.Models.Toolchains;
using Fluence.Core.Models.Workbench;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Problems;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.Debug;
using Fluence.Modules.DotnetCli.Commands;
using Fluence.Modules.DotnetCli.Commands.Project.Build;
using Fluence.Modules.DotnetCli.Commands.Project.Clean;
using Fluence.Modules.DotnetCli.Commands.Project.Restore;
using Fluence.Modules.DotnetCli.Commands.Project.Run;
using Fluence.Modules.DotnetCli.Commands.Project.Test;
using Fluence.Modules.DotnetCli.Commands.Workspace.Build;
using Fluence.Modules.DotnetCli.Commands.Workspace.Clean;
using Fluence.Modules.DotnetCli.Commands.Workspace.Restore;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.Toolchains;

public sealed class CSharpToolchain(
    IServiceProvider services,
    IShellEventBus events) : IDebugToolchain
{
    public string Id => "csharp";

    public string LanguageId => "csharp";

    public ToolchainSupportLevel SupportLevel => ToolchainSupportLevel.Primary;

    public ToolchainCapabilities Capabilities { get; } = new(
        ToolchainCapability.Sdk |
        ToolchainCapability.Build |
        ToolchainCapability.Run |
        ToolchainCapability.Test |
        ToolchainCapability.Restore |
        ToolchainCapability.Clean |
        ToolchainCapability.LanguageServer |
        ToolchainCapability.Debugger);

    public async Task<bool> EnsureAsync(
        ToolchainCapability capability,
        CancellationToken cancellationToken = default)
    {
        if (capability.HasFlag(ToolchainCapability.Sdk) && !await HasSdkAsync(cancellationToken).ConfigureAwait(false))
        {
            events.Publish(new ToolchainProvisioningRequiredEvent(Id, ToolchainCapability.Sdk));
            return false;
        }

        if (capability.HasFlag(ToolchainCapability.LanguageServer) &&
            !services.GetRequiredService<ILspProvisioningService>().IsProvisioned())
        {
            events.Publish(new ToolchainProvisioningRequiredEvent(Id, ToolchainCapability.LanguageServer));
            return false;
        }

        if (capability.HasFlag(ToolchainCapability.Debugger) &&
            !services.GetRequiredService<IDebuggerProvisioningService>().IsProvisioned())
        {
            events.Publish(new ToolchainProvisioningRequiredEvent(Id, ToolchainCapability.Debugger));
            return false;
        }

        return true;
    }

    public async Task ExecuteAsync(ToolchainCommand command, CancellationToken cancellationToken = default)
    {
        switch (command.Kind)
        {
            case ToolchainCommandKind.BuildWorkspace:
                await HandleAsync(new BuildWorkspaceCommand(), cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.TestWorkspace:
                await HandleAsync(new TestWorkspaceCommand(), cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.RestoreWorkspace:
                await HandleAsync(new RestoreWorkspaceCommand(), cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.CleanWorkspace:
                await HandleAsync(new CleanWorkspaceCommand(), cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.BuildProject:
                await HandleAsync(new BuildProjectCommand(RequireProjectPath(command)), cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.TestProject:
                await HandleAsync(new TestProjectCommand(RequireProjectPath(command)), cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.RestoreProject:
                await HandleAsync(new RestoreProjectCommand(RequireProjectPath(command)), cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.CleanProject:
                await HandleAsync(new CleanProjectCommand(RequireProjectPath(command)), cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.RunProject:
                await RunExclusiveAsync(() =>
                    services.GetRequiredService<ICommandHandler<RunProjectCommand>>()
                        .HandleAsync(new RunProjectCommand(), cancellationToken)).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.RunSpecificProject:
                await RunExclusiveAsync(() =>
                    services.GetRequiredService<ICommandHandler<RunSpecificProjectCommand>>()
                        .HandleAsync(new RunSpecificProjectCommand(RequireProjectPath(command)), cancellationToken)).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.DebugProject:
                await services.GetRequiredService<IDebugService>().StartAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.StartLanguageServer:
                await EnsureAsync(ToolchainCapability.LanguageServer, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    public async Task<DebugAdapterSession?> PrepareDebugSessionAsync(CancellationToken ct = default)
    {
        var sdk = services.GetRequiredService<IDotnetSdkProvisioningService>();
        var status = await sdk.GetStatusAsync(ct).ConfigureAwait(false);
        if (!status.IsDotnetAvailable || status.InstalledSdks.Count == 0)
        {
            events.Publish(new ToolchainProvisioningRequiredEvent(Id, ToolchainCapability.Sdk));
            return null;
        }

        var workspace = services.GetRequiredService<IWorkspaceContext>();
        var launchSettings = services.GetRequiredService<ILaunchSettingsService>();
        var processHost = services.GetRequiredService<IProcessHost>();
        var provisioning = services.GetRequiredService<IDebuggerProvisioningService>();
        var adapterFactory = services.GetRequiredService<Func<string, string, CancellationToken, Task<IDebugAdapterClient>>>();
        var output = services.GetRequiredService<IOutputChannelService>();
        var problems = services.GetRequiredService<IProblemService>();
        var targetResolver = services.GetRequiredService<IProjectExecutionTargetResolver>();
        var loc = services.GetRequiredService<ILocalizationService>();
        var settingsService = services.GetRequiredService<ISettingsService>();

        var workspaceRoot = launchSettings.GetWorkspaceRoot(workspace.Current);
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            return null;

        events.Publish(new SelectBottomBarTabEvent(BottomBarTabIds.Run));
        output.Clear(OutputChannelIds.Run);
        problems.ClearSource(ProblemSourceIds.Build);

        var target = await targetResolver.ResolveProjectTargetAsync(ExecutionMode.Debug, ct).ConfigureAwait(false);
        if (target is null)
            return null;

        var dotnet = await sdk.ResolveDotnetExecutableAsync(ct).ConfigureAwait(false);
        var buildArguments = new[] { "build", target.ProjectPath!, "-c", "Debug" };
        var buildWorkingDirectory = Path.GetDirectoryName(target.ProjectPath);
        var buildProblems = new List<ProblemItem>();
        var buildProblemsGate = new object();

        await output.WriteAsync(
            OutputChannelIds.Run,
            $"> {FormatCommand(dotnet, buildArguments)}\r\n",
            cancellationToken: ct).ConfigureAwait(false);

        var buildResult = await processHost.RunWithResultAsync(
            dotnet,
            buildArguments,
            buildWorkingDirectory,
            line => ScheduleBuildOutput(output, line, buildWorkingDirectory, buildProblems, buildProblemsGate, isError: false),
            line => ScheduleBuildOutput(output, line, buildWorkingDirectory, buildProblems, buildProblemsGate, isError: true),
            ct).ConfigureAwait(false);

        problems.ReplaceSource(ProblemSourceIds.Build, buildProblems);
        if (!buildResult.Succeeded || buildProblems.Any(p => p.Severity == ProblemSeverity.Error))
            return null;

        var programPath = ResolveProgramPath(target.ProjectPath, workspaceRoot);
        if (programPath is null)
            return null;

        var configuration = await CreateDebugConfigurationAsync(target, launchSettings, ct).ConfigureAwait(false);
        var workingDirectory = ResolveWorkingDirectory(target.ProjectPath, workspaceRoot, configuration.WorkingDirectory);

        var adapter = await adapterFactory(workspaceRoot, provisioning.GetExecutablePath(), ct).ConfigureAwait(false);

        var launchRequest = new DebugLaunchRequest(
            ProjectPath: target.ProjectPath,
            ProgramPath: programPath,
            WorkingDirectory: workingDirectory,
            Configuration: configuration,
            LaunchArguments: CreateLaunchArguments(programPath, workingDirectory, configuration),
            Breakpoints: [],
            WorkspaceRoot: workspaceRoot);

        var exceptionBreakMode = settingsService.Get<DebugSettings>().ExceptionBreakMode;
        return new DebugAdapterSession(adapter, launchRequest, exceptionBreakMode);
    }

    private static void ScheduleBuildOutput(
        IOutputChannelService output,
        string line,
        string? workingDirectory,
        List<ProblemItem> problems,
        object gate,
        bool isError)
    {
        var problem = MsBuildProblemParser.TryParse(line, workingDirectory);
        if (problem is not null)
        {
            lock (gate)
                problems.Add(problem);
        }

        _ = output.WriteAsync(
            OutputChannelIds.Run,
            line + Environment.NewLine,
            isError ? OutputLogLevel.Error : OutputLogLevel.Information);
    }

    private static async Task<LaunchConfiguration> CreateDebugConfigurationAsync(
        ProjectExecutionTarget target,
        ILaunchSettingsService launchSettings,
        CancellationToken ct)
    {
        var source = target.Configuration ?? new LaunchConfiguration();
        var configuration = new LaunchConfiguration
        {
            Name = source.Name,
            RunProfileName = source.RunProfileName,
            DebugProfileName = source.DebugProfileName,
            Architecture = source.Architecture,
            WorkingDirectory = source.WorkingDirectory,
            Args = source.Args.ToList(),
            Env = new Dictionary<string, string>(source.Env, StringComparer.OrdinalIgnoreCase),
        };

        if (string.IsNullOrWhiteSpace(source.DebugProfileName))
            return configuration;

        var profiles = await launchSettings.DiscoverLaunchProfilesAsync(target.ProjectPath, ct).ConfigureAwait(false);
        var profile = profiles.FirstOrDefault(p =>
            string.Equals(p.Name, source.DebugProfileName, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
            return configuration;

        foreach (var item in profile.EnvironmentVariables)
            configuration.Env[item.Key] = item.Value;

        if (!string.IsNullOrWhiteSpace(profile.ApplicationUrl) &&
            !configuration.Env.ContainsKey("ASPNETCORE_URLS"))
            configuration.Env["ASPNETCORE_URLS"] = profile.ApplicationUrl;

        if (!string.IsNullOrWhiteSpace(profile.CommandLineArgs))
            configuration.Args.InsertRange(0, SplitCommandLine(profile.CommandLineArgs));

        if (!string.IsNullOrWhiteSpace(profile.WorkingDirectory))
            configuration.WorkingDirectory = profile.WorkingDirectory;

        if (!configuration.Env.ContainsKey("ASPNETCORE_ENVIRONMENT"))
            configuration.Env["ASPNETCORE_ENVIRONMENT"] = "Development";
        if (!configuration.Env.ContainsKey("DOTNET_ENVIRONMENT"))
            configuration.Env["DOTNET_ENVIRONMENT"] = "Development";

        return configuration;
    }

    private static JsonObject CreateLaunchArguments(
        string programPath,
        string workingDirectory,
        LaunchConfiguration configuration)
    {
        return new JsonObject
        {
            ["type"] = "coreclr",
            ["program"] = programPath,
            ["cwd"] = workingDirectory,
            ["args"] = new JsonArray(configuration.Args.Select(arg => JsonValue.Create(arg)).ToArray()),
            ["env"] = CreateEnvironmentObject(configuration.Env),
            ["stopAtEntry"] = false,
            ["console"] = "internalConsole",
        };
    }

    private static JsonObject CreateEnvironmentObject(IReadOnlyDictionary<string, string> values)
    {
        var obj = new JsonObject();
        foreach (var pair in values)
            obj[pair.Key] = pair.Value;

        if (!obj.ContainsKey("PATH"))
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrWhiteSpace(path))
                obj["PATH"] = path;
        }

        if (!obj.ContainsKey("DOTNET_ROOT"))
        {
            var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrWhiteSpace(dotnetRoot))
                obj["DOTNET_ROOT"] = dotnetRoot;
        }

        return obj;
    }

    private static string? ResolveProgramPath(string projectPath, string workspaceRoot)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
            return null;

        var document = XDocument.Load(projectPath);
        var targetFramework = FindProperty(document, "TargetFramework") ??
                              FindProperty(document, "TargetFrameworks")?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(targetFramework))
            return null;

        var assemblyName = FindProperty(document, "AssemblyName") ??
                           Path.GetFileNameWithoutExtension(projectPath);
        var candidate = Path.Combine(projectDirectory, "bin", "Debug", targetFramework, assemblyName + ".dll");
        if (File.Exists(candidate))
            return candidate;

        return FindBuildOutput(projectDirectory, workspaceRoot, assemblyName, targetFramework);
    }

    private static string? FindBuildOutput(
        string projectDirectory,
        string workspaceRoot,
        string assemblyName,
        string targetFramework)
    {
        var fileName = assemblyName + ".dll";
        return EnumerateSearchRoots(projectDirectory, workspaceRoot)
            .Where(Directory.Exists)
            .SelectMany(root => SafeEnumerateFiles(root, fileName))
            .Where(path => IsDebugBuildOutput(path, targetFramework))
            .OrderByDescending(HasRuntimeConfig)
            .ThenByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static IEnumerable<string> EnumerateSearchRoots(string projectDirectory, string workspaceRoot)
    {
        yield return projectDirectory;
        if (!string.Equals(projectDirectory, workspaceRoot, StringComparison.OrdinalIgnoreCase))
            yield return workspaceRoot;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root, string fileName)
    {
        try
        {
            return Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}ref{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return [];
        }
    }

    private static bool IsDebugBuildOutput(string path, string targetFramework) =>
        path.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
        path.Contains($"{Path.DirectorySeparatorChar}{targetFramework}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static bool HasRuntimeConfig(string assemblyPath)
    {
        var runtimeConfigPath = Path.Combine(
            Path.GetDirectoryName(assemblyPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(assemblyPath) + ".runtimeconfig.json");
        return File.Exists(runtimeConfigPath);
    }

    private static string? FindProperty(XDocument document, string name) =>
        document.Descendants()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim();

    private static string ResolveWorkingDirectory(
        string projectPath,
        string workspaceRoot,
        string? configuredWorkingDirectory)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath) ?? workspaceRoot;
        if (string.IsNullOrWhiteSpace(configuredWorkingDirectory))
            return projectDirectory;

        return Path.IsPathRooted(configuredWorkingDirectory)
            ? configuredWorkingDirectory
            : Path.GetFullPath(Path.Combine(projectDirectory, configuredWorkingDirectory));
    }

    private static IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < commandLine.Length; i++)
        {
            var ch = commandLine[i];
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (char.IsWhiteSpace(ch) && !inQuotes) { AddCurrent(); continue; }
            current.Append(ch);
        }

        AddCurrent();
        return args;

        void AddCurrent()
        {
            if (current.Length == 0) return;
            args.Add(current.ToString());
            current.Clear();
        }
    }

    private static string FormatCommand(string executable, IReadOnlyList<string> arguments) =>
        string.Join(" ", new[] { QuoteIfNeeded(executable) }.Concat(arguments.Select(QuoteIfNeeded)));

    private static string QuoteIfNeeded(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        return value.Any(char.IsWhiteSpace)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }

    private async Task HandleAsync<TCommand>(TCommand command, CancellationToken cancellationToken)
    {
        if (!await EnsureAsync(ToolchainCapability.Sdk, cancellationToken).ConfigureAwait(false))
            return;

        events.Publish(new SelectBottomBarTabEvent(BottomBarTabIds.Run));
        await services.GetRequiredService<ICommandHandler<TCommand>>()
            .HandleAsync(command, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RunExclusiveAsync(Func<Task> run)
    {
        if (!await EnsureAsync(ToolchainCapability.Sdk).ConfigureAwait(false))
            return;

        var jobs = services.GetRequiredService<IExclusiveJobCoordinator>();
        if (!jobs.TryAcquire(ExclusiveJobKind.Run, out var lease))
        {
            var activeName = jobs.ActiveJob switch
            {
                ExclusiveJobKind.Debug => "debug session",
                ExclusiveJobKind.Run => "run process",
                _ => "job",
            };
            services.GetRequiredService<IUserNotificationService>()
                .ShowWarning("Run", $"Cannot start run while a {activeName} is running.");
            return;
        }

        using (lease!)
        {
            events.Publish(new SelectBottomBarTabEvent(BottomBarTabIds.Run));
            await run().ConfigureAwait(false);
        }
    }

    private async Task<bool> HasSdkAsync(CancellationToken cancellationToken)
    {
        var status = await services.GetRequiredService<IDotnetSdkProvisioningService>()
            .GetStatusAsync(cancellationToken)
            .ConfigureAwait(false);
        return status.IsDotnetAvailable && status.InstalledSdks.Count > 0;
    }

    private static string RequireProjectPath(ToolchainCommand command) =>
        string.IsNullOrWhiteSpace(command.ProjectPath)
            ? throw new ArgumentException("Project path is required.", nameof(command))
            : command.ProjectPath;
}
