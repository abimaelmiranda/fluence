using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Models.Infrastructure;
using Fluence.Modules.DotnetCli.Abstractions.Commands;

namespace Fluence.Modules.DotnetCli.Commands.Project.Run;

public sealed class RunSpecificProjectCommandHandler(ITerminalService terminal)
    : DotnetProjectCommandHandlerBase(terminal), ICommandHandler<RunSpecificProjectCommand>
{
    public Task HandleAsync(RunSpecificProjectCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("run --project", command.ProjectPath, cancellationToken);
}
