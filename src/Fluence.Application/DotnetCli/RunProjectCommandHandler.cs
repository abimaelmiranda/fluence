using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;
using Fluence.Core.Workspace;

namespace Fluence.Application.DotnetCli;

public sealed class RunProjectCommandHandler(IWorkspaceContext workspace, ITerminalService terminal)
    : DotnetCommandHandlerBase(workspace, terminal), ICommandHandler<RunProjectCommand>
{
    public Task HandleAsync(RunProjectCommand command, CancellationToken cancellationToken = default)
    {
        if (workspace.Current.Mode == WorkspaceMode.Solution &&
            !string.IsNullOrWhiteSpace(workspace.Current.StartupProjectPath))
        {
            return terminal.ExecuteAsync(
                $"dotnet run --project \"{workspace.Current.StartupProjectPath}\"",
                System.IO.Path.GetDirectoryName(workspace.Current.StartupProjectPath) ?? System.IO.Directory.GetCurrentDirectory(),
                cancellationToken);
        }

        return RunDotnetAsync("run", cancellationToken);
    }
}
