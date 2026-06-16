using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Abstractions.Theming;
using Fluence.Core.Models.Settings;
using Fluence.Core.Models.Theming;
using Fluence.Core.Services;
using Microsoft.Extensions.Logging;

namespace Fluence.Modules.Settings.Services;

public sealed class ThemeRuntimeCoordinator : IDisposable
{
    private readonly IThemeLoader _themeLoader;
    private readonly ISettingsService _settings;
    private readonly ILogger<ThemeRuntimeCoordinator> _logger;
    private IDisposable? _settingsSubscription;

    public ThemeRuntimeCoordinator(
        IThemeLoader themeLoader,
        ISettingsService settings,
        ILogger<ThemeRuntimeCoordinator> logger)
    {
        _themeLoader = themeLoader;
        _settings = settings;
        _logger = logger;
    }

    public void Start()
    {
        var currentSettings = _settings.Get<GlobalSettings>();
        ApplyOnUiThread(_themeLoader.Load(currentSettings.Theme), currentSettings);
        _settingsSubscription = _settings.Watch<GlobalSettings>()
            .Subscribe(new ActionObserver<GlobalSettings>(
                onNext: settings => ApplyOnUiThread(_themeLoader.Load(settings.Theme), settings),
                onError: ex => _logger.LogError(ex, "Unhandled error in settings watcher")));
    }

    private void ApplyOnUiThread(IdeTheme theme, GlobalSettings settings)
    {
        if (Dispatcher.UIThread.CheckAccess())
            Apply(theme, settings);
        else
            Dispatcher.UIThread.Post(() => Apply(theme, settings));
    }

    public void Dispose()
    {
        _settingsSubscription?.Dispose();
    }

    private void Apply(IdeTheme theme, GlobalSettings settings)
    {
        var app = Application.Current;
        if (app is null)
            return;

        var colors = theme.Colors;
        SetColor(app, "FluenceColorShell", colors.Shell);
        SetColor(app, "FluenceColorSurface", colors.Surface);
        SetColor(app, "FluenceColorSurfaceElevated", colors.SurfaceElevated);
        SetColor(app, "FluenceColorInputBackground", colors.InputBackground);
        SetColor(app, "FluenceColorBorder", colors.Border);
        SetColor(app, "FluenceColorTextPrimary", colors.TextPrimary);
        SetColor(app, "FluenceColorTextSecondary", colors.TextSecondary);
        SetColor(app, "FluenceColorAccent", colors.Accent);
        SetColor(app, "FluenceColorError", colors.Error);
        SetColor(app, "FluenceColorEditorBackground", colors.EditorBackground);
        SetColor(app, "FluenceColorEditorForeground", colors.EditorForeground);
        SetColor(app, "FluenceColorEditorSelection", colors.EditorSelection);
        SetColor(app, "FluenceColorActivityBarBackground", colors.ActivityBarBackground);
        SetColor(app, "FluenceColorActivityBarActiveBackground", colors.ActivityBarActiveBackground);
        SetColor(app, "FluenceColorActivityBarForeground", colors.ActivityBarForeground);
        SetColor(app, "FluenceColorActivityBarInactiveForeground", colors.ActivityBarInactiveForeground);
        SetColor(app, "FluenceColorBottomBarBackground", colors.BottomBarBackground);

        SetBrush(app, "FluenceBrushShell", colors.Shell);
        SetBrush(app, "FluenceBrushSurface", colors.Surface);
        SetBrush(app, "FluenceBrushSurfaceElevated", colors.SurfaceElevated);
        SetBrush(app, "FluenceBrushInputBackground", colors.InputBackground);
        SetBrush(app, "FluenceBrushBorder", colors.Border);
        SetBrush(app, "FluenceBrushTextPrimary", colors.TextPrimary);
        SetBrush(app, "FluenceBrushTextSecondary", colors.TextSecondary);
        SetBrush(app, "FluenceBrushAccent", colors.Accent);
        SetBrush(app, "FluenceBrushError", colors.Error);
        SetBrush(app, "FluenceBrushEditorBackground", colors.EditorBackground);
        SetBrush(app, "FluenceBrushEditorForeground", colors.EditorForeground);
        SetBrush(app, "FluenceBrushEditorSelection", colors.EditorSelection);
        SetBrush(app, "FluenceBrushActivityBarBackground", colors.ActivityBarBackground);
        SetBrush(app, "FluenceBrushActivityBarActiveBackground", colors.ActivityBarActiveBackground);
        SetBrush(app, "FluenceBrushActivityBarForeground", colors.ActivityBarForeground);
        SetBrush(app, "FluenceBrushActivityBarInactiveForeground", colors.ActivityBarInactiveForeground);
        SetBrush(app, "FluenceBrushBottomBarBackground", colors.BottomBarBackground);

        app.Resources["FluenceFontFamily"] = new FontFamily(settings.FontFamily);
        app.Resources["FluenceFontSizeBody"] = Math.Max(8, settings.FontSize);
        app.Resources["FluenceFontSizeSmall"] = Math.Max(8, settings.FontSize - 1);
    }

    private void SetColor(Application app, string key, string color)
    {
        try
        {
            app.Resources[key] = Color.Parse(color);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid color value '{Color}' for resource '{Key}'", color, key);
        }
    }

    private void SetBrush(Application app, string key, string color)
    {
        Color parsed;
        try
        {
            parsed = Color.Parse(color);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid color value '{Color}' for brush '{Key}'", color, key);
            return;
        }

        if (app.Resources.TryGetValue(key, out var value) && value is SolidColorBrush brush)
        {
            brush.Color = parsed;
            return;
        }

        app.Resources[key] = new SolidColorBrush(parsed);
    }
}
