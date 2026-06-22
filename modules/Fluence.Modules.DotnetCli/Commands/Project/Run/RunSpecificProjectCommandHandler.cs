using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Projects;

namespace Fluence.Modules.DotnetCli.Commands.Project.Run;

public sealed class RunSpecificProjectCommandHandler(
    IRunService run)
    : ICommandHandler<RunSpecificProjectCommand>
{
    public Task HandleAsync(RunSpecificProjectCommand command, CancellationToken cancellationToken = default)
        => run.RunSpecificAsync(command.ProjectPath, cancellationToken);
}
