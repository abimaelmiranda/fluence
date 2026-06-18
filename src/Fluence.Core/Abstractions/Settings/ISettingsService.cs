using System;
using System.Collections.Generic;

namespace Fluence.Core.Abstractions.Settings;

public interface ISettingsService
{
    TSettings Get<TSettings>()
        where TSettings : class, new();

    object Get(Type settingsType);

    void Update<TSettings>(Action<TSettings> updateAction)
        where TSettings : class, new();

    void Update(Type settingsType, Action<object> updateAction);

    void ReplaceSection(Type settingsType, IReadOnlyDictionary<string, object?> values);

    void Reload();

    void ResetAll();

    IObservable<TSettings> Watch<TSettings>()
        where TSettings : class, new();

    IObservable<object> Watch(Type settingsType);
}
