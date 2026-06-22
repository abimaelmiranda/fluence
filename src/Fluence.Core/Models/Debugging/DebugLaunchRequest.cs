using System.Text.Json.Nodes;
using Fluence.Core.Models.Workspace;

namespace Fluence.Core.Models.Debugging;

public sealed record DebugLaunchRequest(
    string ProjectPath,
    string ProgramPath,
    string WorkingDirectory,
    LaunchConfiguration Configuration,
    JsonObject LaunchArguments,
    IReadOnlyList<DebugBreakpoint> Breakpoints,
    string WorkspaceRoot);
