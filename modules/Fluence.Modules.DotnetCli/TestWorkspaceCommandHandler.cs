using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;
using Fluence.Core.Workspace;

namespace Fluence.Modules.DotnetCli;

public sealed class TestWorkspaceCommandHandler(IWorkspaceContext workspace, ITerminalService terminal)
    : DotnetCommandHandlerBase(workspace, terminal), ICommandHandler<TestWorkspaceCommand>
{
    public Task HandleAsync(TestWorkspaceCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("test", cancellationToken);
}
