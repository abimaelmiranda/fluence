using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Modules.SolutionView.Abstractions;

namespace Fluence.Modules.SolutionView.Commands.AddProjectReference;

public sealed class AddProjectReferencesCommandHandler(IProjectReferenceService references)
    : ICommandHandler<AddProjectReferencesCommand>
{
    public Task HandleAsync(AddProjectReferencesCommand command, CancellationToken cancellationToken = default)
        => references.AddProjectReferencesAsync(command.ProjectPath, command.ReferencedProjectPaths, cancellationToken);
}