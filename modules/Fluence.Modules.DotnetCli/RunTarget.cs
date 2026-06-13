namespace Fluence.Modules.DotnetCli;

internal sealed record RunTarget(
    string Command,
    string WorkingDirectory,
    RunTargetKind Kind);
