using Fluence.Core.Workspace;

namespace Fluence.Core.Debug;

public sealed record DebugLaunchRequest(
    string ProjectPath,
    string ProgramPath,
    string WorkingDirectory,
    LaunchConfiguration Configuration,
    IReadOnlyList<DebugBreakpoint> Breakpoints,
    string WorkspaceRoot);
