using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Fluence.Core.Models.Workspace;

public sealed class LaunchSettings
{
    public string StartupProject { get; set; } = string.Empty;

    public List<LaunchConfiguration> Configurations { get; set; } = [new LaunchConfiguration()];

    [JsonIgnore]
    public LaunchConfiguration DefaultConfiguration =>
        Configurations.Count > 0 ? Configurations[0] : new LaunchConfiguration();
}
