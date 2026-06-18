using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Models.Output;
using Fluence.Modules.DotnetCli.Services;

namespace Fluence.Modules.DotnetCli.Abstractions.Commands;

public abstract class DotnetProjectCommandHandlerBase(
    IProcessHost processHost,
    IOutputChannelService output,
    IDotnetSdkProvisioningService sdk)
{
    protected async Task RunDotnetAsync(
        string subcommand,
        string projectPath,
        CancellationToken cancellationToken,
        Action<string, string?>? inspectLine = null)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return;
        }

        var workingDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return;
        }

        var dotnet = await sdk.ResolveDotnetExecutableAsync(cancellationToken);
        var arguments = CreateArguments(subcommand, projectPath);
        await output.WriteAsync(OutputChannelIds.Run, $"> {DotnetCommandLine.Format(dotnet, arguments)}{System.Environment.NewLine}", cancellationToken: cancellationToken);
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

    private static IReadOnlyList<string> CreateArguments(string subcommand, string projectPath)
    {
        var arguments = new List<string>(subcommand.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        arguments.Add(projectPath);
        return arguments;
    }
}
