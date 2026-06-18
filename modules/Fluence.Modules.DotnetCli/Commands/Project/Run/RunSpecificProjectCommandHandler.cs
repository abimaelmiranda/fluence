using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Output;
using Fluence.Modules.DotnetCli.Abstractions.Commands;

namespace Fluence.Modules.DotnetCli.Commands.Project.Run;

public sealed class RunSpecificProjectCommandHandler(
    IProcessHost processHost,
    IOutputChannelService output,
    IDotnetSdkProvisioningService sdk)
    : DotnetProjectCommandHandlerBase(processHost, output, sdk), ICommandHandler<RunSpecificProjectCommand>
{
    public Task HandleAsync(RunSpecificProjectCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("run --project", command.ProjectPath, cancellationToken);
}
