using System;
using System.Resources;

namespace Fluence.Core.Abstractions.Localization;

public interface ILocalizationService
{
    string Get(string key);

    void Register(ResourceManager manager);

    void SetLanguage(string cultureName);

    event Action LanguageChanged;
}
