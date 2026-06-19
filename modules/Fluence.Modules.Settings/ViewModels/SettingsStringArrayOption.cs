namespace Fluence.Modules.Settings.ViewModels;

public sealed record SettingsStringArrayOption(
    string Id,
    string Title,
    string Description,
    string Category)
{
    public string DisplayText => $"{Id} - {Title}";
}
