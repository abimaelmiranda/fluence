namespace Fluence.Core.Models.Settings;

[SettingsSection("global", useGlobalSettings: false)]
public sealed class GlobalSettings
{
    public string FontFamily { get; set; } = "Inter, Segoe UI, SF Pro Text";

    public double FontSize { get; set; } = 13;

    [ThemeReference]
    public string Theme { get; set; } = "FluenceDark";

    public string Language { get; set; } = "en";
}
