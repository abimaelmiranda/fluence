using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Fluence.Modules.Settings.ViewModels;

public sealed partial class SettingsStringArrayItemViewModel(
    string value,
    Action<SettingsStringArrayItemViewModel> removeAction) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(Description))]
    [NotifyPropertyChangedFor(nameof(Category))]
    [NotifyPropertyChangedFor(nameof(IsKnownRule))]
    private string _value = value;

    public string Title => RoslynRuleCatalog.Find(Value)?.Title ?? "Manual diagnostic ID";

    public string Description => RoslynRuleCatalog.Find(Value)?.Description ?? "Suppresses this diagnostic ID when Roslyn or OmniSharp reports it.";

    public string Category => RoslynRuleCatalog.Find(Value)?.Category ?? "Custom";

    public bool IsKnownRule => RoslynRuleCatalog.Find(Value) is not null;

    [RelayCommand]
    private void Remove() => removeAction(this);
}
