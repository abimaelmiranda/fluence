using Fluence.Core.Models.Workspace;

namespace Fluence.Core.Models.Debugging;

public sealed record DebugLaunchRequest(
    string ProjectPath,
    string ProgramPath,
    string WorkingDirectory,
    LaunchConfiguration Configuration,
    IReadOnlyList<DebugBreakpoint> Breakpoints,
    string WorkspaceRoot);
