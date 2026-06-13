using Fluence.Core.Workspace;

namespace Fluence.Modules.Debug;

public interface IDebugSessionManager
{
    DebugSession? CurrentSession { get; }

    void Prepare(ProjectExecutionTarget target);
}
