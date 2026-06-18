namespace Fluence.Core.Models.Settings;

[SettingsSection("problems")]
public sealed class ProblemsSettings
{
    public bool ShowWarningsFromAllFiles { get; set; }

    public bool ShowInformationFromAllFiles { get; set; }

    public int MaxVisibleProblems { get; set; } = 500;
}
