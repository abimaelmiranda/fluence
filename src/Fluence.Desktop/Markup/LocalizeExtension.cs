using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Fluence.Desktop.Markup;

public sealed class LocalizeExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding($"[{Key}]")
        {
            Source = Locale.Current,
            Mode = BindingMode.OneWay,
        };
}
