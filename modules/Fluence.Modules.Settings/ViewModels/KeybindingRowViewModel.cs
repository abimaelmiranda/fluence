using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Fluence.Modules.Settings.ViewModels;

public sealed partial class KeybindingRowViewModel(
    string commandId,
    string title,
    string scope,
    string key,
    bool hasConflict,
    SettingsToolViewModel owner) : ObservableObject
{
    private readonly SettingsToolViewModel _owner = owner;

    public string CommandId { get; } = commandId;

    public string Title { get; } = title;

    public string Scope { get; } = scope;

    public bool HasConflict { get; } = hasConflict;

    [ObservableProperty]
    private string _key = key;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordButtonText))]
    private bool _isRecording;

    public string RecordButtonText => IsRecording ? "Press keys..." : "Record";

    public void HandleCapture(string? gesture, bool isEscape)
    {
        if (isEscape)
        {
            CancelCapture();
            return;
        }

        if (string.IsNullOrWhiteSpace(gesture))
            return;

        Key = gesture;
        IsRecording = false;
    }

    public void Capture(string gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
            return;

        Key = gesture;
        IsRecording = false;
    }

    public void CancelCapture() => IsRecording = false;

    [RelayCommand]
    private void StartRecording() => IsRecording = true;

    [RelayCommand]
    private void Reset() => _owner.ResetKeybinding(CommandId);
}
