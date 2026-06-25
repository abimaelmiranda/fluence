using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Toolchains;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Events.Toolchains;
using Fluence.Core.Events.Ui;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Output;
using Fluence.Core.Models.Toolchains;
using Fluence.Core.Models.Workbench;

namespace Fluence.Modules.Toolchains;

public sealed class CppToolchain(
    string languageId,
    CppRunService runService,
    CppDebuggerProvisioningService debugger,
    IDebugService debugService,
    IWorkspaceContext workspace,
    Func<string, string, CancellationToken, Task<IDebugAdapterClient>> adapterFactory,
    IProcessHost processHost,
    IOutputChannelService output,
    IShellEventBus events,
    IUserNotificationService notifications) : IDebugToolchain
{
    public string Id => languageId;

    public string LanguageId => languageId;

    public ToolchainSupportLevel SupportLevel => ToolchainSupportLevel.Experimental;

    public ToolchainCapabilities Capabilities { get; } = new(ToolchainCapability.Run | ToolchainCapability.Debugger);

    public Task<bool> EnsureAsync(
        ToolchainCapability capability,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        if (!Capabilities.Has(capability))
        {
            notifications.ShowWarning(
                "C/C++",
                $"Capability '{capability}' is not wired for the experimental C/C++ toolchain yet.");
            return Task.FromResult(false);
        }

        if (capability.HasFlag(ToolchainCapability.Debugger) && !debugger.IsProvisioned())
        {
            events.Publish(new ToolchainProvisioningRequiredEvent(Id, ToolchainCapability.Debugger));
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    public async Task ExecuteAsync(ToolchainCommand command, CancellationToken cancellationToken = default)
    {
        switch (command.Kind)
        {
            case ToolchainCommandKind.RunProject:
                await runService.RunAsync(cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.RunSpecificProject:
                await runService.RunSpecificAsync(RequireProjectPath(command), cancellationToken).ConfigureAwait(false);
                break;
            case ToolchainCommandKind.DebugProject:
                await debugService.StartAsync(cancellationToken).ConfigureAwait(false);
                break;
            default:
                notifications.ShowWarning(
                    "C/C++",
                    $"'{command.Kind}' is not supported yet by the experimental C/C++ toolchain.");
                break;
        }
    }

    public async Task<DebugAdapterSession?> PrepareDebugSessionAsync(CancellationToken ct = default)
    {
        if (!debugger.IsProvisioned())
        {
            notifications.ShowWarning("Debug", "lldb-dap is not available. Use toolchain setup to install C/C++ debugging support.");
            return null;
        }

        var context = CppProjectLocator.ResolveFromWorkspace(workspace);
        if (context is null)
        {
            notifications.ShowWarning("Debug", "No C or C++ project root was found.");
            return null;
        }

        if (!context.HasCMakeLists)
        {
            notifications.ShowWarning("Debug", "Only CMake-based C and C++ projects are supported for debug right now.");
            return null;
        }

        events.Publish(new SelectBottomBarTabEvent(BottomBarTabIds.Run));

        if (!await ConfigureAndBuildAsync(context, ct).ConfigureAwait(false))
            return null;

        var executable = CppProjectLocator.FindExecutablePath(context.BuildDirectory, context);
        if (executable is null)
        {
            notifications.ShowWarning("Debug", "The native executable could not be located after building.");
            return null;
        }

        var workspaceRoot = context.ProjectRoot;
        var adapter = await adapterFactory(workspaceRoot, debugger.GetExecutablePath(), ct).ConfigureAwait(false);

        var request = new DebugLaunchRequest(
            ProjectPath: context.ProjectRoot,
            ProgramPath: executable,
            WorkingDirectory: context.ProjectRoot,
            Configuration: new LaunchConfiguration
            {
                Architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            },
            LaunchArguments: CreateLaunchArguments(executable, context.ProjectRoot),
            Breakpoints: [],
            WorkspaceRoot: workspaceRoot);

        return new DebugAdapterSession(adapter, request, DebugExceptionBreakMode.OnlyUserUnhandled);
    }

    private async Task<bool> ConfigureAndBuildAsync(CppProjectContext context, CancellationToken ct)
    {
        var cmake = ToolchainPlatform.FindOnPath("cmake");
        if (cmake is null)
        {
            notifications.ShowWarning("Debug", "cmake was not found on PATH.");
            return false;
        }

        var buildDir = context.BuildDirectory;
        Directory.CreateDirectory(buildDir);

        var configureArgs = new List<string>
        {
            "-S", context.ProjectRoot,
            "-B", buildDir,
            "-DCMAKE_EXPORT_COMPILE_COMMANDS=ON",
            "-DCMAKE_BUILD_TYPE=Debug",
        };

        await output.WriteAsync(OutputChannelIds.Run, $"> {FormatCommand(cmake, configureArgs)}{Environment.NewLine}", cancellationToken: ct).ConfigureAwait(false);
        var configureResult = await processHost.RunWithResultAsync(
            cmake,
            configureArgs,
            context.ProjectRoot,
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine),
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine, OutputLogLevel.Error),
            ct).ConfigureAwait(false);

        if (!configureResult.Succeeded)
            return false;

        var buildArgs = new List<string> { "--build", buildDir, "--config", "Debug" };
        await output.WriteAsync(OutputChannelIds.Run, $"> {FormatCommand(cmake, buildArgs)}{Environment.NewLine}", cancellationToken: ct).ConfigureAwait(false);
        var buildResult = await processHost.RunWithResultAsync(
            cmake,
            buildArgs,
            context.ProjectRoot,
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine),
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine, OutputLogLevel.Error),
            ct).ConfigureAwait(false);

        return buildResult.Succeeded;
    }

    private static JsonObject CreateLaunchArguments(string programPath, string workingDirectory) =>
        new()
        {
            ["type"] = "lldb-dap",
            ["program"] = programPath,
            ["cwd"] = workingDirectory,
            ["stopOnEntry"] = false,
            ["args"] = new JsonArray(),
        };

    private static string FormatCommand(string executable, IReadOnlyList<string> arguments) =>
        "\"" + executable.Replace("\"", "\\\"", StringComparison.Ordinal) + "\" " +
        string.Join(" ", arguments.Select(arg => "\"" + arg.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""));

    private static string RequireProjectPath(ToolchainCommand command) =>
        string.IsNullOrWhiteSpace(command.ProjectPath)
            ? throw new ArgumentException("Project path is required.", nameof(command))
            : command.ProjectPath;
}
