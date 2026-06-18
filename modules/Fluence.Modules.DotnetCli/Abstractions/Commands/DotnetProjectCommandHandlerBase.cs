using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Models.Output;

namespace Fluence.Modules.DotnetCli.Abstractions.Commands;

public abstract class DotnetProjectCommandHandlerBase(
    IProcessHost processHost,
    IOutputChannelService output,
    IDotnetSdkProvisioningService sdk)
{
    protected async Task RunDotnetAsync(string subcommand, string projectPath, CancellationToken cancellationToken)
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
        var arguments = $"{subcommand} \"{projectPath}\"";
        await output.WriteAsync(OutputChannelIds.Run, $"> {Quote(dotnet)} {arguments}{System.Environment.NewLine}", cancellationToken: cancellationToken);
        await processHost.RunAsync(
            dotnet,
            arguments,
            workingDirectory,
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + System.Environment.NewLine),
            line => _ = output.WriteAsync(OutputChannelIds.Run, line + System.Environment.NewLine, OutputChannelEntryKind.Error),
            cancellationToken);
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\\\"", System.StringComparison.Ordinal) + "\"";
}
