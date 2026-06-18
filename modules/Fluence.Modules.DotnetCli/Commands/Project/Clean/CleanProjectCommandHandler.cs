using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Output;
using Fluence.Modules.DotnetCli.Abstractions.Commands;

namespace Fluence.Modules.DotnetCli.Commands.Project.Clean;

public sealed class CleanProjectCommandHandler(
    IProcessHost processHost,
    IOutputChannelService output,
    IDotnetSdkProvisioningService sdk)
    : DotnetProjectCommandHandlerBase(processHost, output, sdk), ICommandHandler<CleanProjectCommand>
{
    public Task HandleAsync(CleanProjectCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("clean", command.ProjectPath, cancellationToken);
}
