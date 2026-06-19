using System;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using Fluence.Core.Abstractions.Localization;

namespace Fluence.Core.Services.Localization;

public sealed class LocalizationService : ILocalizationService
{
    private readonly List<ResourceManager> _managers = [];
    private CultureInfo _culture = CultureInfo.InvariantCulture;

    public event Action? LanguageChanged;

    public string Get(string key)
    {
        foreach (var manager in _managers)
        {
            try
            {
                var value = manager.GetString(key, _culture);
                if (value is not null)
                    return value;
            }
            catch (MissingManifestResourceException) { }
        }

        return key;
    }

    public void Register(ResourceManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        _managers.Add(manager);
    }

    public void SetLanguage(string cultureName)
    {
        CultureInfo newCulture;
        if (string.IsNullOrWhiteSpace(cultureName) || cultureName.Equals("en", StringComparison.OrdinalIgnoreCase))
        {
            newCulture = CultureInfo.InvariantCulture;
        }
        else
        {
            try
            {
                newCulture = CultureInfo.GetCultureInfo(cultureName);
            }
            catch (CultureNotFoundException)
            {
                newCulture = CultureInfo.InvariantCulture;
            }
        }

        if (Equals(newCulture, _culture))
            return;

        _culture = newCulture;
        LanguageChanged?.Invoke();
    }
}
