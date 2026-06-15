using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;

namespace Fluence.Modules.Debug.Models;

public sealed record DebugSession(
    bool IsActive,
    ExecutionMode ActiveMode,
    string TargetArchitecture,
    int? ProcessId,
    ProjectExecutionTarget Target);
