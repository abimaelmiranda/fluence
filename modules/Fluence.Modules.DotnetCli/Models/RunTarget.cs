using System.Collections.Generic;

namespace Fluence.Modules.DotnetCli.Models;

internal sealed record RunTarget(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string>? Environment,
    RunTargetKind Kind);
