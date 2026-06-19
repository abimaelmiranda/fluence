// Convenience re-export so Desktop XAML files can use xmlns:l="using:Fluence.Desktop.Markup"
// for both {l:Loc} and {x:Static l:Locale.Current}.
// The actual implementation lives in Fluence.Core.Services.Localization.Locale.
using Fluence.Core.Abstractions.Localization;
using CoreLocale = Fluence.Core.Services.Localization.Locale;

namespace Fluence.Desktop.Markup;

public static class Locale
{
    public static CoreLocale Current => CoreLocale.Current;

    public static void Initialize(ILocalizationService service) =>
        CoreLocale.Initialize(service);
}
