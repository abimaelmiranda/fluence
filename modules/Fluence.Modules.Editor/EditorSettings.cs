using Fluence.Core.Models.Settings;

namespace Fluence.Modules.Editor;

[SettingsSection("editor")]
public sealed class EditorSettings
{
    public string FontFamily { get; set; } = "Menlo,Consolas,Cascadia Mono,monospace";

    public double FontSize { get; set; } = 13;

    public bool ShowLineNumbers { get; set; } = true;

    public bool AutoPairBrackets { get; set; } = true;

    public bool FormatOnSave { get; set; } = true;

    public int CompletionTriggerDelayMs { get; set; } = 120;
}
