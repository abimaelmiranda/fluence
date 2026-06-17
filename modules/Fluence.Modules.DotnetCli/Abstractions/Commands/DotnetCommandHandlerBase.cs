using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Models.Infrastructure;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;

namespace Fluence.Modules.DotnetCli.Abstractions.Commands;

public abstract class DotnetCommandHandlerBase(
    IWorkspaceContext workspace,
    ITerminalService terminal,
    IDotnetSdkProvisioningService sdk)
{
    protected IWorkspaceContext Workspace { get; } = workspace;

    protected ITerminalService Terminal { get; } = terminal;

    protected async Task RunDotnetAsync(string subcommand, CancellationToken cancellationToken)
    {
        var workingDirectory = Workspace.Current.CurrentFolderPath
                               ?? Path.GetDirectoryName(Workspace.Current.CurrentSolutionPath);

        if (string.IsNullOrEmpty(workingDirectory))
        {
            return;
        }

        var targetPath = Workspace.Current.CurrentSolutionPath ?? workingDirectory;
        var dotnet = await sdk.ResolveDotnetExecutableAsync(cancellationToken);
        await Terminal.ExecuteAsync($"{Quote(dotnet)} {subcommand} \"{targetPath}\"", workingDirectory, cancellationToken);
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\\\"", System.StringComparison.Ordinal) + "\"";
}
