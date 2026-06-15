using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Core.Abstractions.Workspace;

public interface IProjectExecutionTargetResolver
{
    Task<ProjectExecutionTarget?> ResolveProjectTargetAsync(
        ExecutionMode mode,
        CancellationToken cancellationToken = default);
}
