using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Projects;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Events.Ui;
using Fluence.Core.Models.Output;
using Fluence.Core.Models.Workbench;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Infrastructure.Projects;

public sealed class CppRunService(
    IWorkspaceContext workspace,
    IProcessHost processHost,
    IOutputChannelService output,
    IUserNotificationService notifications,
    IShellEventBus events) : IRunService
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var context = CppProjectLocator.ResolveFromWorkspace(workspace);
        if (context is null)
        {
            notifications.ShowWarning(
                "Run",
                "No C or C++ project root was found. Add a CMakeLists.txt or a .vcxproj and retry.");
            return;
        }

        if (!context.HasCMakeLists)
        {
            notifications.ShowWarning(
                "Run",
                "Only CMake-based C and C++ projects are supported for run right now.");
            return;
        }

        await RunContextAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public async Task RunSpecificAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var context = CppProjectLocator.ResolveFromPath(projectPath);
        if (context is null)
        {
            notifications.ShowWarning(
                "Run",
                $"Unable to resolve a C or C++ project from '{projectPath}'.");
            return;
        }

        await RunContextAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunContextAsync(CppProjectContext context, CancellationToken cancellationToken)
    {
        events.Publish(new SelectBottomBarTabEvent(BottomBarTabIds.Run));
        output.Clear(OutputChannelIds.Run);

        var cmake = ResolveCmakeExecutable();
        if (cmake is null)
        {
            notifications.ShowWarning("Run", "cmake was not found on PATH.");
            return;
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

        await output.WriteAsync(
            OutputChannelIds.Run,
            $"> {FormatCommand(cmake, configureArgs)}{Environment.NewLine}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var configureResult = await processHost.RunWithResultAsync(
            cmake,
            configureArgs,
            context.ProjectRoot,
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine),
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine, OutputChannelEntryKind.Error),
            cancellationToken).ConfigureAwait(false);
        if (!configureResult.Succeeded)
            return;

        var buildArgs = new List<string> { "--build", buildDir, "--config", "Debug" };
        await output.WriteAsync(
            OutputChannelIds.Run,
            $"> {FormatCommand(cmake, buildArgs)}{Environment.NewLine}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var buildResult = await processHost.RunWithResultAsync(
            cmake,
            buildArgs,
            context.ProjectRoot,
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine),
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine, OutputChannelEntryKind.Error),
            cancellationToken).ConfigureAwait(false);
        if (!buildResult.Succeeded)
            return;

        var executable = ResolveExecutablePath(context, buildDir);
        if (executable is null)
        {
            notifications.ShowWarning("Run", "The native executable could not be located after building.");
            return;
        }

        await output.WriteAsync(
            OutputChannelIds.Run,
            $"> {FormatCommand(executable, Array.Empty<string>())}{Environment.NewLine}",
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await processHost.RunAsync(
            executable,
            Array.Empty<string>(),
            Path.GetDirectoryName(executable),
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine),
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + Environment.NewLine, OutputChannelEntryKind.Error),
            cancellationToken).ConfigureAwait(false);
    }

    private static string FormatCommand(string executable, IReadOnlyList<string> arguments) =>
        arguments.Count == 0
            ? Quote(executable)
            : Quote(executable) + " " + string.Join(" ", arguments.Select(Quote));

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string? ResolveCmakeExecutable() =>
        PlatformTooling.Current.FindOnPath("cmake");

    private static string? ResolveExecutablePath(CppProjectContext context, string buildDir)
    {
        return CppProjectLocator.FindExecutablePath(buildDir, context);
    }
}
