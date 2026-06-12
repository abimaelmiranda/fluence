using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;

namespace Fluence.Application.DotnetCli;

public sealed class TestProjectCommandHandler(ITerminalService terminal)
    : DotnetProjectCommandHandlerBase(terminal), ICommandHandler<TestProjectCommand>
{
    public Task HandleAsync(TestProjectCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("test", command.ProjectPath, cancellationToken);
}
