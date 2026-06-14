namespace Fluence.Modules.DotnetCli.Models;

internal sealed record RunTarget(
    string Command,
    string WorkingDirectory,
    RunTargetKind Kind);
