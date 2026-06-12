using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;
using Fluence.Core.Workspace;

namespace Fluence.Modules.DotnetCli;

public sealed class CleanWorkspaceCommandHandler(IWorkspaceContext workspace, ITerminalService terminal)
    : DotnetCommandHandlerBase(workspace, terminal), ICommandHandler<CleanWorkspaceCommand>
{
    public Task HandleAsync(CleanWorkspaceCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("clean", cancellationToken);
}
