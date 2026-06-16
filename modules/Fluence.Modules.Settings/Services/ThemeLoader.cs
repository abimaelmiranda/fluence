using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Avalonia.Media;
using Avalonia.Platform;
using Fluence.Core.Abstractions.Storage;
using Fluence.Core.Abstractions.Theming;
using Fluence.Core.Models.Theming;
using Fluence.Core.Services;
using Microsoft.Extensions.Logging;

namespace Fluence.Modules.Settings.Services;

public sealed class ThemeLoader : IThemeLoader
{
    public const string BuiltInThemeName = "FluenceDark";

    private static readonly IReadOnlyDictionary<string, string> BuiltInSemanticTokenColors =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["class"] = "#4EC9B0",
            ["delegateName"] = "#4EC9B0",
            ["record"] = "#4EC9B0",
            ["interface"] = "#B8D7A3",
            ["struct"] = "#86C691",
            ["recordStruct"] = "#86C691",
            ["enum"] = "#B8D7A3",
            ["enumMember"] = "#51B6C4",
            ["typeParameter"] = "#B8D7A3",
            ["method"] = "#DCDCAA",
            ["extensionMethod"] = "#DCDCAA",
            ["event"] = "#DCDCAA",
            ["property"] = "#9CDCFE",
            ["field"] = "#D4D4D4",
            ["staticSymbol"] = "#51B6C4",
            ["local"] = "#9CDCFE",
            ["parameter"] = "#9CDCFE",
        };

    private readonly IFluenceStorageService _storage;
    private readonly ILogger<ThemeLoader> _logger;
    private readonly ObservableValue<IdeTheme> _observable = new();
    private FileSystemWatcher? _themesWatcher;
    private Timer? _debounceTimer;
    private string? _currentThemeReference;

    public ThemeLoader(IFluenceStorageService storage, ILogger<ThemeLoader> logger)
    {
        _storage = storage;
        _logger = logger;
        CurrentTheme = CreateBuiltInTheme();
        _observable.Publish(CurrentTheme);
        InitializeThemesWatcher();
    }

    public IdeTheme CurrentTheme { get; private set; }

    public IdeTheme Load(string? themeReference)
    {
        _currentThemeReference = themeReference;
        CurrentTheme = TryLoad(themeReference) ?? CreateBuiltInTheme();
        _observable.Publish(CurrentTheme);
        return CurrentTheme;
    }

    public IReadOnlyList<ThemeDescriptor> GetAvailableThemes()
    {
        var themes = new List<ThemeDescriptor>
        {
            new(BuiltInThemeName, BuiltInThemeName, true),
        };

        var themesDirectory = _storage.GetUserPath("themes");
        if (!Directory.Exists(themesDirectory))
            return themes;

        foreach (var path in Directory.EnumerateFiles(themesDirectory, "*.json").OrderBy(Path.GetFileName))
        {
            try
            {
                var theme = ParseTheme(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
                themes.Add(new ThemeDescriptor(Path.GetFileName(path), theme.Name, false, path));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping malformed theme file: {Path}", path);
            }
        }

        return themes;
    }

    public ThemeDescriptor InstallTheme(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("Theme file was not found.", sourcePath);

        var json = File.ReadAllText(sourcePath);
        var parsed = ParseTheme(json, Path.GetFileNameWithoutExtension(sourcePath));
        var themesDirectory = _storage.GetUserPath("themes");
        Directory.CreateDirectory(themesDirectory);

        var fileName = CreateThemeFileName(parsed.Name, sourcePath);
        var targetPath = Path.Combine(themesDirectory, fileName);
        File.WriteAllText(targetPath, json);

        return new ThemeDescriptor(fileName, parsed.Name, false, targetPath);
    }

    public IObservable<IdeTheme> Watch() => _observable;

    public void Dispose()
    {
        _themesWatcher?.Dispose();
        _debounceTimer?.Dispose();
    }

    private void InitializeThemesWatcher()
    {
        var themesDirectory = _storage.GetUserPath("themes");
        try
        {
            Directory.CreateDirectory(themesDirectory);
            _themesWatcher = new FileSystemWatcher(themesDirectory, "*.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            _themesWatcher.Changed += OnThemeFileChanged;
            _themesWatcher.Created += OnThemeFileChanged;
            _themesWatcher.Deleted += OnThemeFileChanged;
            _themesWatcher.Renamed += OnThemeFileRenamed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not initialize themes directory watcher at {Path}", themesDirectory);
        }
    }

    private void OnThemeFileChanged(object sender, FileSystemEventArgs e) => ScheduleReload();

    private void OnThemeFileRenamed(object sender, RenamedEventArgs e) => ScheduleReload();

    private void ScheduleReload()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(_ => ReloadCurrentTheme(), null, 150, Timeout.Infinite);
    }

    private void ReloadCurrentTheme()
    {
        if (string.IsNullOrWhiteSpace(_currentThemeReference) ||
            string.Equals(_currentThemeReference.Trim(), BuiltInThemeName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _logger.LogDebug("Theme file changed on disk; reloading theme '{Reference}'", _currentThemeReference);
        Load(_currentThemeReference);
    }

    private IdeTheme? TryLoad(string? themeReference)
    {
        if (string.IsNullOrWhiteSpace(themeReference) ||
            string.Equals(themeReference.Trim(), BuiltInThemeName, StringComparison.OrdinalIgnoreCase))
        {
            return CreateBuiltInTheme();
        }

        var path = ResolveThemePath(themeReference.Trim());
        if (path is null || !File.Exists(path))
        {
            _logger.LogWarning("Theme '{Reference}' could not be resolved; falling back to built-in theme", themeReference);
            return null;
        }

        try
        {
            return ParseTheme(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse theme '{Path}'; falling back to built-in theme", path);
            return null;
        }
    }

    private string? ResolveThemePath(string themeReference)
    {
        if (Path.IsPathRooted(themeReference))
            return themeReference;

        var candidate = _storage.GetUserPath(Path.Combine("themes", themeReference));
        if (File.Exists(candidate))
            return candidate;

        return Path.HasExtension(candidate) ? candidate : candidate + ".json";
    }

    private IdeTheme ParseTheme(string json, string fallbackName)
    {
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("Theme root must be a JSON object.");

        var defaults = CreateBuiltInTheme();
        var colors = root["colors"] as JsonObject;
        var font = root["font"] as JsonObject;
        var semanticColors = ParseSemanticTokenColors(root["semanticTokenColors"] as JsonObject, defaults.SemanticTokenColors);

        var textMateJson = root["textMate"]?.ToJsonString() ?? json;

        return new IdeTheme
        {
            Name = GetString(root, "name") ?? fallbackName,
            Font = new ThemeFont
            {
                Family = GetString(font, "family") ?? defaults.Font.Family,
                Size = GetDouble(font, "size") ?? defaults.Font.Size,
            },
            Colors = new ThemeColors
            {
                Shell            = GetColorWithFallback(colors, "shell.background",              null,                          defaults.Colors.Shell),
                Surface          = GetColorWithFallback(colors, "surface.background",            "sideBar.background",          defaults.Colors.Surface),
                SurfaceElevated  = GetColorWithFallback(colors, "surface.elevated",              null,                          defaults.Colors.SurfaceElevated),
                InputBackground  = GetColorWithFallback(colors, "input.background",              null,                          defaults.Colors.InputBackground),
                Border           = GetColorWithFallback(colors, "border",                        null,                          defaults.Colors.Border),
                TextPrimary      = GetColorWithFallback(colors, "text.primary",                  "foreground",                  defaults.Colors.TextPrimary),
                TextSecondary    = GetColorWithFallback(colors, "text.secondary",                "descriptionForeground",       defaults.Colors.TextSecondary),
                Accent           = GetColorWithFallback(colors, "accent",                        "focusBorder",                 defaults.Colors.Accent),
                Error            = GetColorWithFallback(colors, "error",                         "errorForeground",             defaults.Colors.Error),
                EditorBackground = GetColorWithFallback(colors, "editor.background",             null,                          defaults.Colors.EditorBackground),
                EditorForeground = GetColorWithFallback(colors, "editor.foreground",             null,                          defaults.Colors.EditorForeground),
                EditorSelection  = GetColorWithFallback(colors, "editor.selection",              "editor.selectionBackground",  defaults.Colors.EditorSelection),
                ActivityBarBackground         = GetColorWithFallback(colors, "activityBar.background",         null, defaults.Colors.ActivityBarBackground),
                ActivityBarActiveBackground   = GetColorWithFallback(colors, "activityBar.activeBg",           "activityBar.activeBackground",   defaults.Colors.ActivityBarActiveBackground),
                ActivityBarForeground         = GetColorWithFallback(colors, "activityBar.foreground",         null, defaults.Colors.ActivityBarForeground),
                ActivityBarInactiveForeground = GetColorWithFallback(colors, "activityBar.inactiveFg",         "activityBar.inactiveForeground", defaults.Colors.ActivityBarInactiveForeground),
                BottomBarBackground           = GetColorWithFallback(colors, "bottomBar.background",           null, defaults.Colors.BottomBarBackground),
            },
            SemanticTokenColors = semanticColors,
            TextMateThemeJson = textMateJson,
        };
    }

    private static IdeTheme CreateBuiltInTheme()
    {
        return new IdeTheme
        {
            Name = BuiltInThemeName,
            Font = new ThemeFont
            {
                Family = "Inter, Segoe UI, SF Pro Text",
                Size = 13,
            },
            Colors = new ThemeColors
            {
                Shell = "#1E1E1E",
                Surface = "#1E1E1E",
                SurfaceElevated = "#282828",
                InputBackground = "#282828",
                Border = "#282828",
                TextPrimary = "#F2F5F8",
                TextSecondary = "#AAB4BF",
                Accent = "#B05CDA",
                Error = "#FF6B6B",
                EditorBackground = "#1E1E1E",
                EditorForeground = "#D4D4D4",
                EditorSelection = "#3A3D41",
                ActivityBarBackground = "#1E1E1E",
                ActivityBarActiveBackground = "#282828",
                ActivityBarForeground = "#F2F5F8",
                ActivityBarInactiveForeground = "#AAB4BF",
                BottomBarBackground = "#602676",
            },
            SemanticTokenColors = BuiltInSemanticTokenColors,
            TextMateThemeJson = LoadBuiltInTextMateTheme(),
        };
    }

    private static string LoadBuiltInTextMateTheme()
    {
        try
        {
            var uri = new Uri("avares://Fluence.Modules.Editor/Assets/Themes/standard_dark.json");
            using var stream = AssetLoader.Open(uri);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return """
            {
              "name": "FluenceDark",
              "settings": [
                {
                  "settings": {
                    "background": "#1E1E1E",
                    "foreground": "#D4D4D4",
                    "selection": "#3A3D41"
                  }
                }
              ]
            }
            """;
        }
    }

    private static IReadOnlyDictionary<string, string> ParseSemanticTokenColors(
        JsonObject? source,
        IReadOnlyDictionary<string, string> defaults)
    {
        var result = new Dictionary<string, string>(defaults, StringComparer.OrdinalIgnoreCase);
        if (source is null)
            return result;

        foreach (var item in source)
        {
            var color = item.Value switch
            {
                JsonValue value => TryGetColor(value),
                JsonObject obj => GetColor(obj, "foreground", string.Empty),
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(color))
                result[item.Key] = color;
        }

        return result;
    }

    private static string? TryGetColor(JsonValue value)
    {
        try
        {
            return NormalizeColor(value.GetValue<string>());
        }
        catch
        {
            return null;
        }
    }

    private static string? GetString(JsonObject? source, string key)
    {
        if (source is null)
            return null;

        try
        {
            return source[key]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private static double? GetDouble(JsonObject? source, string key)
    {
        if (source is null)
            return null;

        try
        {
            return source[key]?.GetValue<double>();
        }
        catch
        {
            return null;
        }
    }

    private static string GetColor(JsonObject? source, string key, string fallback)
    {
        var value = GetString(source, key);
        return NormalizeColor(value) ?? fallback;
    }

    private static string GetColorWithFallback(JsonObject? source, string preferredKey, string? legacyKey, string fallback)
    {
        var value = GetString(source, preferredKey)
            ?? (legacyKey is not null ? GetString(source, legacyKey) : null);
        return NormalizeColor(value) ?? fallback;
    }

    private static string? NormalizeColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            var color = Color.Parse(value.Trim());
            return color.A == 255
                ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
                : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        catch
        {
            return null;
        }
    }

    private static string CreateThemeFileName(string themeName, string sourcePath)
    {
        var baseName = string.IsNullOrWhiteSpace(themeName)
            ? Path.GetFileNameWithoutExtension(sourcePath)
            : themeName.Trim();

        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(baseName
            .Select(ch => invalid.Contains(ch) ? '-' : ch)
            .ToArray());

        safe = safe.Replace(' ', '-').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(safe))
            safe = "custom-theme";

        return safe.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? safe : safe + ".json";
    }
}
