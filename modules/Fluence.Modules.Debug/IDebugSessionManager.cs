using Fluence.Core.Workspace;

namespace Fluence.Modules.Debug;

public interface IDebugSessionManager
{
    DebugSession? CurrentSession { get; }

    void Start(ProjectExecutionTarget target, ExecutionMode mode);

    void Stop();
}
