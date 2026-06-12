using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;
using Fluence.Core.Workspace;

namespace Fluence.Application.DotnetCli;

public sealed class BuildWorkspaceCommandHandler(IWorkspaceContext workspace, ITerminalService terminal)
    : DotnetCommandHandlerBase(workspace, terminal), ICommandHandler<BuildWorkspaceCommand>
{
    public Task HandleAsync(BuildWorkspaceCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("build", cancellationToken);
}
