using System.Collections.Generic;

namespace Fluence.Core.Models.Workspace;

public sealed class LaunchConfiguration
{
    public string Name { get; set; } = "Default";

    public string? RunProfileName { get; set; }

    public string? DebugProfileName { get; set; }

    public List<string> Args { get; set; } = [];

    public Dictionary<string, string> Env { get; set; } = new();

    public string? WorkingDirectory { get; set; }

    public string Architecture { get; set; } = "x64";
}
