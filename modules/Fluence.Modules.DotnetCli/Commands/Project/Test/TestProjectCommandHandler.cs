using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;
using Fluence.Modules.DotnetCli.Abstractions.Commands;

namespace Fluence.Modules.DotnetCli.Commands.Project.Test;

public sealed class TestProjectCommandHandler(ITerminalService terminal)
    : DotnetProjectCommandHandlerBase(terminal), ICommandHandler<TestProjectCommand>
{
    public Task HandleAsync(TestProjectCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("test", command.ProjectPath, cancellationToken);
}
