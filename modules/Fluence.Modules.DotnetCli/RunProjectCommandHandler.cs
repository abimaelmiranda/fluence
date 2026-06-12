using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;
using Fluence.Core.Workspace;

namespace Fluence.Modules.DotnetCli;

public sealed class RunProjectCommandHandler(IWorkspaceContext workspace, ITerminalService terminal)
    : DotnetCommandHandlerBase(workspace, terminal), ICommandHandler<RunProjectCommand>
{
    public Task HandleAsync(RunProjectCommand command, CancellationToken cancellationToken = default)
    {
        if (Workspace.Current.Mode == WorkspaceMode.Solution &&
            !string.IsNullOrWhiteSpace(Workspace.Current.StartupProjectPath))
        {
            return Terminal.ExecuteAsync(
                $"dotnet run --project \"{Workspace.Current.StartupProjectPath}\"",
                System.IO.Path.GetDirectoryName(Workspace.Current.StartupProjectPath) ?? System.IO.Directory.GetCurrentDirectory(),
                cancellationToken);
        }

        return RunDotnetAsync("run", cancellationToken);
    }
}
