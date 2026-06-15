using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.Debug.Models;

namespace Fluence.Modules.Debug.Abstractions.Session;

public interface IDebugSessionManager
{
    DebugSession? CurrentSession { get; }

    void Start(ProjectExecutionTarget target, ExecutionMode mode);

    void Stop();
}
