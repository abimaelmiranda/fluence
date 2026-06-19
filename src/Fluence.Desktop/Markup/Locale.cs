using System.ComponentModel;
using Fluence.Core.Abstractions.Localization;

namespace Fluence.Desktop.Markup;

public sealed class Locale : INotifyPropertyChanged
{
    public static readonly Locale Current = new();

    private ILocalizationService? _service;

    private Locale() { }

    public static void Initialize(ILocalizationService service)
    {
        Current._service = service;
        service.LanguageChanged += Current.OnLanguageChanged;
    }

    public string this[string key] => _service?.Get(key) ?? key;

    private void OnLanguageChanged()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));

    public event PropertyChangedEventHandler? PropertyChanged;
}
