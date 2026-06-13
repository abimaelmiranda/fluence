using Fluence.Core.Workspace;

namespace Fluence.Modules.Debug;

public sealed class DebugSessionManager : IDebugSessionManager
{
    public DebugSession? CurrentSession { get; private set; }

    public void Prepare(ProjectExecutionTarget target)
    {
        CurrentSession = new DebugSession(target);
    }
}
