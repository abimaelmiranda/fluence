using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;
using Fluence.Core.Workspace;
using Fluence.Modules.DotnetCli.Abstractions.Commands;

namespace Fluence.Modules.DotnetCli.Commands.Workspace.Test;

public sealed class TestWorkspaceCommandHandler(IWorkspaceContext workspace, ITerminalService terminal)
    : DotnetCommandHandlerBase(workspace, terminal), ICommandHandler<TestWorkspaceCommand>
{
    public Task HandleAsync(TestWorkspaceCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("test", cancellationToken);
}
