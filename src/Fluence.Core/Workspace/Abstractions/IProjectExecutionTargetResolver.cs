namespace Fluence.Core.Workspace;

public interface IProjectExecutionTargetResolver
{
    Task<ProjectExecutionTarget?> ResolveProjectTargetAsync(
        ExecutionMode mode,
        CancellationToken cancellationToken = default);
}
