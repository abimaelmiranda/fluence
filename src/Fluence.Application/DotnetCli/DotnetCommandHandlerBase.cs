using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Infrastructure;
using Fluence.Core.Workspace;

namespace Fluence.Application.DotnetCli;

public abstract class DotnetCommandHandlerBase(IWorkspaceContext workspace, ITerminalService terminal)
{
    protected async Task RunDotnetAsync(string subcommand, CancellationToken cancellationToken)
    {
        var workingDirectory = workspace.Current.CurrentFolderPath
                               ?? Path.GetDirectoryName(workspace.Current.CurrentSolutionPath);

        if (string.IsNullOrEmpty(workingDirectory))
        {
            return;
        }

        var targetPath = workspace.Current.CurrentSolutionPath ?? workingDirectory;
        await terminal.ExecuteAsync($"dotnet {subcommand} \"{targetPath}\"", workingDirectory, cancellationToken);
    }
}
