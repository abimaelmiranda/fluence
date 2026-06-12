using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;
using Fluence.Core.Workspace;

namespace Fluence.Modules.DotnetCli;

public sealed class RestoreWorkspaceCommandHandler(IWorkspaceContext workspace, ITerminalService terminal)
    : DotnetCommandHandlerBase(workspace, terminal), ICommandHandler<RestoreWorkspaceCommand>
{
    public Task HandleAsync(RestoreWorkspaceCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("restore", cancellationToken);
}
