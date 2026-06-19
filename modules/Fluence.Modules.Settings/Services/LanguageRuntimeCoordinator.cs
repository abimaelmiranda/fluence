using System;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Models.Settings;
using Fluence.Core.Services;

namespace Fluence.Modules.Settings.Services;

public sealed class LanguageRuntimeCoordinator(
    ILocalizationService localization,
    ISettingsService settings) : IDisposable
{
    private IDisposable? _subscription;

    public void Start()
    {
        _subscription = settings.Watch<GlobalSettings>()
            .Subscribe(new ActionObserver<GlobalSettings>(
                onNext: s => localization.SetLanguage(s.Language)));
    }

    public void Dispose() => _subscription?.Dispose();
}
