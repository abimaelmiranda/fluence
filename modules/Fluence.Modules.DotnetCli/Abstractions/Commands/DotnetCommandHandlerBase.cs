using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Infrastructure;
using Fluence.Core.Workspace;

namespace Fluence.Modules.DotnetCli.Abstractions.Commands;

public abstract class DotnetCommandHandlerBase(IWorkspaceContext workspace, ITerminalService terminal)
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
        await Terminal.ExecuteAsync($"dotnet {subcommand} \"{targetPath}\"", workingDirectory, cancellationToken);
    }
}
