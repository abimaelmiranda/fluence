using System.Collections.Generic;

namespace Fluence.Core.Workspace;

public sealed class LaunchConfiguration
{
    public string Name { get; set; } = "Default";

    public List<string> Args { get; set; } = [];

    public Dictionary<string, string> Env { get; set; } = new();

    public string Architecture { get; set; } = "x64";
}
