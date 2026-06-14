using Fluence.Core.Commands;
using Fluence.Modules.SolutionView.Abstractions;

namespace Fluence.Modules.SolutionView.Commands.RemoveProjectReference;

public sealed class RemoveProjectReferenceCommandHandler(IProjectReferenceService references)
    : ICommandHandler<RemoveProjectReferenceCommand>
{
    public Task HandleAsync(RemoveProjectReferenceCommand command, CancellationToken cancellationToken = default)
        => references.RemoveProjectReferenceAsync(command.ProjectPath, command.ReferencedProjectPath, cancellationToken);
}