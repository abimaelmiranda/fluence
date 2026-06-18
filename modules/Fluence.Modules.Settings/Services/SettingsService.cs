using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Abstractions.Storage;
using Fluence.Core.Models.Settings;
using Fluence.Core.Services;

namespace Fluence.Modules.Settings.Services;

public sealed class SettingsService : ISettingsService, IDisposable
{
    private const string SettingsFileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly ISettingsRegistry _registry;
    private readonly string _path;
    private readonly Dictionary<Type, object> _cache = new();
    private readonly Dictionary<Type, object> _observables = new();
    private readonly Dictionary<Type, ObservableValue<object>> _objectObservables = new();
    private readonly FileSystemWatcher? _watcher;
    private Timer? _reloadTimer;
    private JsonObject _document = new();
    private DateTime _lastSavedAtUtc = DateTime.MinValue;
    private bool _disposed;

    public SettingsService(ISettingsRegistry registry, IFluenceStorageService storage)
    {
        _registry = registry;
        _path = storage.GetUserPath(SettingsFileName);
        LoadDocument();

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            _watcher = new FileSystemWatcher(directory, SettingsFileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnSettingsFileChanged;
            _watcher.Created += OnSettingsFileChanged;
            _watcher.Renamed += OnSettingsFileChanged;
        }
    }

    public TSettings Get<TSettings>()
        where TSettings : class, new() =>
        (TSettings)Get(typeof(TSettings));

    public object Get(Type settingsType)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(settingsType, out var cached))
                return cached;

            var resolved = Resolve(settingsType);
            EnsureSectionDefaults(settingsType, resolved);
            _cache[settingsType] = resolved;
            return resolved;
        }
    }

    public void Update<TSettings>(Action<TSettings> updateAction)
        where TSettings : class, new() =>
        Update(typeof(TSettings), value => updateAction((TSettings)value));

    public void Update(Type settingsType, Action<object> updateAction)
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        lock (_gate)
        {
            var section = _registry.GetSection(settingsType);
            var value = Resolve(settingsType);
            updateAction(value);
            _document[section.SectionName] = JsonSerializer.SerializeToNode(value, section.TypeInfo);
            _cache[settingsType] = value;
            SaveDocument();
            Publish(settingsType, value);
        }
    }

    public void ReplaceSection(Type settingsType, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        lock (_gate)
        {
            var section = _registry.GetSection(settingsType);
            var options = section.TypeInfo.Options;
            var sectionObject = new JsonObject();

            foreach (var property in GetWritableProperties(settingsType))
            {
                if (!values.TryGetValue(property.Name, out var value) || IsFallbackValue(value))
                    continue;

                sectionObject[ToJsonName(property.Name)] = JsonSerializer.SerializeToNode(value, property.PropertyType, options);
            }

            if (sectionObject.Count == 0)
                _document.Remove(section.SectionName);
            else
                _document[section.SectionName] = sectionObject;

            _cache.Remove(settingsType);
            SaveDocument();
            Publish(settingsType, Resolve(settingsType));
        }
    }

    public IObservable<TSettings> Watch<TSettings>()
        where TSettings : class, new()
    {
        lock (_gate)
        {
            var type = typeof(TSettings);
            if (!_observables.TryGetValue(type, out var observable))
            {
                var typed = new ObservableValue<TSettings>();
                typed.Publish((TSettings)Get(type));
                _observables[type] = typed;
                observable = typed;
            }

            return (IObservable<TSettings>)observable;
        }
    }

    public void Reload()
    {
        lock (_gate)
        {
            LoadDocument();
            _cache.Clear();
            var changed = false;
            foreach (var section in _registry.Sections)
            {
                var resolved = Resolve(section.SettingsType);
                changed |= EnsureSectionDefaults(section.SettingsType, resolved, saveImmediately: false);
                Publish(section.SettingsType, resolved);
            }

            if (changed)
                SaveDocument();
        }
    }

    public void ResetAll()
    {
        lock (_gate)
        {
            _document = new JsonObject();
            _cache.Clear();
            foreach (var section in _registry.Sections)
            {
                var resolved = Resolve(section.SettingsType);
                EnsureSectionDefaults(section.SettingsType, resolved, saveImmediately: false);
                Publish(section.SettingsType, resolved);
            }

            SaveDocument();
        }
    }

    public IObservable<object> Watch(Type settingsType)
    {
        lock (_gate)
        {
            if (!_objectObservables.TryGetValue(settingsType, out var observable))
            {
                observable = new ObservableValue<object>();
                observable.Publish(Get(settingsType));
                _objectObservables[settingsType] = observable;
            }

            return observable;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _watcher?.Dispose();
        _reloadTimer?.Dispose();
    }

    private void OnSettingsFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_gate)
        {
            if (DateTime.UtcNow - _lastSavedAtUtc < TimeSpan.FromMilliseconds(500))
                return;

            _reloadTimer ??= new Timer(_ => ReloadFromDisk(), null, Timeout.Infinite, Timeout.Infinite);
            _reloadTimer.Change(TimeSpan.FromMilliseconds(150), Timeout.InfiniteTimeSpan);
        }
    }

    private void ReloadFromDisk()
    {
        Reload();
    }

    private void LoadDocument()
    {
        if (!File.Exists(_path))
        {
            _document = new JsonObject();
            return;
        }

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(_path));
            _document = node as JsonObject ?? new JsonObject();
        }
        catch
        {
            _document = new JsonObject();
        }
    }

    private void SaveDocument()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, _document.ToJsonString(JsonOptions));
        _lastSavedAtUtc = DateTime.UtcNow;
    }

    private object Resolve(Type settingsType)
    {
        var section = _registry.GetSection(settingsType);
        var settings = Activator.CreateInstance(settingsType)
            ?? throw new InvalidOperationException($"Could not create {settingsType.FullName}.");

        var localProperties = _document[section.SectionName] is JsonObject localObject
            ? ApplyObject(settings, settingsType, localObject, section.TypeInfo.Options)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (section.UseGlobalSettings && settingsType != typeof(GlobalSettings))
        {
            var global = (GlobalSettings)Resolve(typeof(GlobalSettings));
            ApplyGlobalFallback(settings, settingsType, section.SectionName, global, localProperties);
        }

        return settings;
    }

    private bool EnsureSectionDefaults(Type settingsType, object resolved, bool saveImmediately = true)
    {
        var section = _registry.GetSection(settingsType);
        var sectionObject = _document[section.SectionName] as JsonObject;
        if (sectionObject is null)
        {
            _document[section.SectionName] = JsonSerializer.SerializeToNode(resolved, section.TypeInfo);
            if (saveImmediately)
                SaveDocument();

            return true;
        }

        var changed = false;
        foreach (var property in GetWritableProperties(settingsType))
        {
            var key = ToJsonName(property.Name);
            if (sectionObject.ContainsKey(key) || sectionObject.ContainsKey(property.Name))
                continue;

            sectionObject[key] = JsonSerializer.SerializeToNode(
                property.GetValue(resolved),
                property.PropertyType,
                section.TypeInfo.Options);
            changed = true;
        }

        if (changed && saveImmediately)
            SaveDocument();

        return changed;
    }

    private static HashSet<string> ApplyObject(object target, Type targetType, JsonObject source, JsonSerializerOptions options)
    {
        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in GetWritableProperties(targetType))
        {
            var key = ToJsonName(property.Name);
            var node = source[key] ?? source[property.Name];
            if (node is null || node.GetValueKind() == JsonValueKind.Null)
                continue;

            try
            {
                var value = node.Deserialize(property.PropertyType, options);
                if (IsFallbackValue(value))
                    continue;

                property.SetValue(target, value);
                applied.Add(property.Name);
            }
            catch
            {
                // Invalid user values are ignored so one bad property does not disable the section.
            }
        }

        return applied;
    }

    private static void ApplyGlobalFallback(
        object target,
        Type targetType,
        string sectionName,
        GlobalSettings global,
        IReadOnlySet<string> localProperties)
    {
        var defaultInstance = Activator.CreateInstance(targetType);
        if (defaultInstance is null)
            return;

        var globalType = typeof(GlobalSettings);
        foreach (var property in GetWritableProperties(targetType))
        {
            if (localProperties.Contains(property.Name))
                continue;

            if (string.Equals(sectionName, "editor", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(property.Name, "FontFamily", StringComparison.Ordinal))
                continue;

            var globalProperty = globalType.GetProperty(property.Name, BindingFlags.Public | BindingFlags.Instance);
            if (globalProperty is null || globalProperty.PropertyType != property.PropertyType)
                continue;

            var current = property.GetValue(target);
            var defaultValue = property.GetValue(defaultInstance);
            if (!Equals(current, defaultValue))
                continue;

            property.SetValue(target, globalProperty.GetValue(global));
        }
    }

    private void Publish(Type settingsType, object value)
    {
        if (_observables.TryGetValue(settingsType, out var observable))
        {
            var method = observable.GetType().GetMethod(nameof(ObservableValue<object>.Publish));
            method?.Invoke(observable, new[] { value });
        }

        if (_objectObservables.TryGetValue(settingsType, out var objectObservable))
            objectObservable.Publish(value);
    }

    private static IEnumerable<PropertyInfo> GetWritableProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0);

    private static bool IsFallbackValue(object? value) =>
        value is null || value is string text && string.IsNullOrWhiteSpace(text);

    private static string ToJsonName(string propertyName) =>
        string.IsNullOrEmpty(propertyName)
            ? propertyName
            : char.ToLowerInvariant(propertyName[0]) + propertyName[1..];
}
