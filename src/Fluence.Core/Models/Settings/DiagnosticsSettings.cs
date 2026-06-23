namespace Fluence.Core.Models.Settings;

[SettingsSection("diagnostics")]
public sealed class DiagnosticsSettings
{
    public bool EnableMemoryMonitor { get; set; } = false;

    public int MemoryMonitorIntervalSeconds { get; set; } = 30;
}
