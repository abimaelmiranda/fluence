using Fluence.Core.Models.Settings;

namespace Fluence.Modules.SourceControl;

[SettingsSection("sourceControl")]
public sealed class SourceControlSettings
{
    public int RefreshIntervalSeconds { get; set; } = 5;
}
