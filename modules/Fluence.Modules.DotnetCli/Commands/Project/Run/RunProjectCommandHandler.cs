using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Projects;

namespace Fluence.Modules.DotnetCli.Commands.Project.Run;

public sealed class RunProjectCommandHandler(
    IRunService run)
    : ICommandHandler<RunProjectCommand>
{
    public async Task HandleAsync(RunProjectCommand command, CancellationToken cancellationToken = default)
        => await run.RunAsync(cancellationToken).ConfigureAwait(false);
}
