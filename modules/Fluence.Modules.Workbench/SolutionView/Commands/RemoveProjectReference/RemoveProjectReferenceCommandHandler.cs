using Fluence.Core.Abstractions.Commands;
using Fluence.Modules.Workbench.SolutionView.Abstractions;

namespace Fluence.Modules.Workbench.SolutionView.Commands.RemoveProjectReference;

public sealed class RemoveProjectReferenceCommandHandler(IProjectReferenceService references)
    : ICommandHandler<RemoveProjectReferenceCommand>
{
    public Task HandleAsync(RemoveProjectReferenceCommand command, CancellationToken cancellationToken = default)
        => references.RemoveProjectReferenceAsync(command.ProjectPath, command.ReferencedProjectPath, cancellationToken);
}