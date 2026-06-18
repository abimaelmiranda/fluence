namespace Fluence.Core.Abstractions.Settings;

public interface ISettingsTool
{
    bool IsRecordingKeybinding { get; }

    void ShowSettings();

    void ShowKeybindings();
}
