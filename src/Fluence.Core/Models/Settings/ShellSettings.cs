namespace Fluence.Core.Models.Settings;

[SettingsSection("shell")]
public sealed class ShellSettings
{
    public double SidebarWidth { get; set; } = 300;

    public double TerminalHeight { get; set; } = 160;

    public bool AnimationsEnabled { get; set; } = true;
}
