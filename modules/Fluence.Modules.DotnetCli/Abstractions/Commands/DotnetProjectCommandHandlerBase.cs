using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Models.Infrastructure;

namespace Fluence.Modules.DotnetCli.Abstractions.Commands;

public abstract class DotnetProjectCommandHandlerBase(
    ITerminalService terminal,
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
        await terminal.ExecuteAsync($"{Quote(dotnet)} {subcommand} \"{projectPath}\"", workingDirectory, cancellationToken);
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\\\"", System.StringComparison.Ordinal) + "\"";
}
