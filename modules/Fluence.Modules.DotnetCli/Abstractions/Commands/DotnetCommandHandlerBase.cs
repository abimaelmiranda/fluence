using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Models.Output;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;

namespace Fluence.Modules.DotnetCli.Abstractions.Commands;

public abstract class DotnetCommandHandlerBase(
    IWorkspaceContext workspace,
    IProcessHost processHost,
    IOutputChannelService output,
    IDotnetSdkProvisioningService sdk)
{
    protected IWorkspaceContext Workspace { get; } = workspace;

    protected async Task RunDotnetAsync(
        string subcommand,
        CancellationToken cancellationToken,
        Action<string, string?>? inspectLine = null)
    {
        var workingDirectory = Workspace.Current.CurrentFolderPath
                               ?? Path.GetDirectoryName(Workspace.Current.CurrentSolutionPath);

        if (string.IsNullOrEmpty(workingDirectory))
        {
            return;
        }

        var targetPath = Workspace.Current.CurrentSolutionPath ?? workingDirectory;
        var dotnet = await sdk.ResolveDotnetExecutableAsync(cancellationToken);
        var arguments = $"{subcommand} \"{targetPath}\"";
        await output.WriteAsync(OutputChannelIds.Run, $"> {Quote(dotnet)} {arguments}{System.Environment.NewLine}", cancellationToken: cancellationToken);
        void OnOutput(string line)
        {
            inspectLine?.Invoke(line, workingDirectory);
            _ = output.WriteAsync(OutputChannelIds.Run, line + System.Environment.NewLine);
        }

        void OnError(string line)
        {
            inspectLine?.Invoke(line, workingDirectory);
            _ = output.WriteAsync(OutputChannelIds.Run, line + System.Environment.NewLine, OutputChannelEntryKind.Error);
        }

        await processHost.RunAsync(
            dotnet,
            arguments,
            workingDirectory,
            OnOutput,
            OnError,
            cancellationToken);
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\\\"", System.StringComparison.Ordinal) + "\"";
}
