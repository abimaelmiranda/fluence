using System.Collections.Generic;

namespace Fluence.Modules.DotnetCli.Models;

internal sealed record RunTarget(
    string Executable,
    string Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment,
    RunTargetKind Kind);
