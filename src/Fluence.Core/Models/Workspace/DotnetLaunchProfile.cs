using System.Collections.Generic;

namespace Fluence.Core.Models.Workspace;

public sealed record DotnetLaunchProfile(
    string Name,
    string CommandName,
    string? ApplicationUrl,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    string? CommandLineArgs,
    string? WorkingDirectory);
