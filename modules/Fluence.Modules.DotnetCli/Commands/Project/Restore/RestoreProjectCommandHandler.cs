using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Output;
using Fluence.Modules.DotnetCli.Abstractions.Commands;

namespace Fluence.Modules.DotnetCli.Commands.Project.Restore;

public sealed class RestoreProjectCommandHandler(
    IProcessHost processHost,
    IOutputChannelService output,
    IDotnetSdkProvisioningService sdk)
    : DotnetProjectCommandHandlerBase(processHost, output, sdk), ICommandHandler<RestoreProjectCommand>
{
    public Task HandleAsync(RestoreProjectCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("restore", command.ProjectPath, cancellationToken);
}
