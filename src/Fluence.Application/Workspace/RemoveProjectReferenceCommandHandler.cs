using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Commands;

namespace Fluence.Application.Workspace;

public sealed class RemoveProjectReferenceCommandHandler(IProjectReferenceService references)
    : ICommandHandler<RemoveProjectReferenceCommand>
{
    public Task HandleAsync(RemoveProjectReferenceCommand command, CancellationToken cancellationToken = default)
        => references.RemoveProjectReferenceAsync(command.ProjectPath, command.ReferencedProjectPath, cancellationToken);
}
