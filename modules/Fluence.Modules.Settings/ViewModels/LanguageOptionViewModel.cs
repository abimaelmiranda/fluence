namespace Fluence.Modules.Settings.ViewModels;

public sealed class LanguageOptionViewModel(string code, string displayName)
{
    public string Code { get; } = code;

    public string DisplayName { get; } = displayName;

    public override string ToString() => DisplayName;
}
