using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Output;
using Fluence.Modules.DotnetCli.Abstractions.Commands;

namespace Fluence.Modules.DotnetCli.Commands.Project.Test;

public sealed class TestProjectCommandHandler(
    IProcessHost processHost,
    IOutputChannelService output,
    IDotnetSdkProvisioningService sdk)
    : DotnetProjectCommandHandlerBase(processHost, output, sdk), ICommandHandler<TestProjectCommand>
{
    public Task HandleAsync(TestProjectCommand command, CancellationToken cancellationToken = default)
        => RunDotnetAsync("test", command.ProjectPath, cancellationToken);
}
