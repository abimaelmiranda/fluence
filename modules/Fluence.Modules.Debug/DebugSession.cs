using Fluence.Core.Workspace;

namespace Fluence.Modules.Debug;

public sealed record DebugSession(
    bool IsActive,
    ExecutionMode ActiveMode,
    string StartupProjectId,
    string TargetArchitecture,
    int? ProcessId,
    ProjectExecutionTarget Target);
