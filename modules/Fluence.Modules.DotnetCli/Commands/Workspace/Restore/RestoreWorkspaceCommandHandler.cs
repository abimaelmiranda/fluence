using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;
using Fluence.Core.Workspace;
using Fluence.Modules.DotnetCli.Abstractions.Commands;

namespace Fluence.Modules.DotnetCli.Commands.Workspace.Restore;

public sealed class RestoreWorkspaceCommandHandler(IWorkspaceContext workspace, ITerminalService terminal)
    : DotnetCommandHandlerBase(workspace, terminal), ICommandHandler<RestoreWorkspaceCommand>
{
    public Task HandleAsync(RestoreWorkspaceCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("restore", cancellationToken);
}
