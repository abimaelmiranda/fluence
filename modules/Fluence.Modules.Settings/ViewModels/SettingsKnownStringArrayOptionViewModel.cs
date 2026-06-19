using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Fluence.Modules.Settings.ViewModels;

public sealed partial class SettingsKnownStringArrayOptionViewModel : ObservableObject
{
    private readonly Action<SettingsKnownStringArrayOptionViewModel, bool> _setSuppressed;

    public SettingsKnownStringArrayOptionViewModel(
        SettingsStringArrayOption option,
        bool isSuppressed,
        Action<SettingsKnownStringArrayOptionViewModel, bool> setSuppressed)
    {
        Id = option.Id;
        Title = option.Title;
        Description = option.Description;
        Category = option.Category;
        _isSuppressed = isSuppressed;
        _setSuppressed = setSuppressed;
    }

    public string Id { get; }

    public string Title { get; }

    public string Description { get; }

    public string Category { get; }

    public string DisplayText => $"{Id} - {Title}";

    [ObservableProperty]
    private bool _isSuppressed;

    partial void OnIsSuppressedChanged(bool value) => _setSuppressed(this, value);

    public void SetSuppressedSilently(bool value) => SetProperty(ref _isSuppressed, value, nameof(IsSuppressed));
}
