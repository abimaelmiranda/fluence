using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Keybindings;
using Fluence.Core.Abstractions.Storage;
using Fluence.Core.Models.Keybindings;
using Fluence.Core.Services;
using Fluence.Core.Services.Keybindings;

namespace Fluence.Modules.Settings.Services;

public sealed class KeybindingService : IKeybindingService, IDisposable
{
    private const string KeybindingsFileName = "keybindings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly ICommandRegistry _commands;
    private readonly string _path;
    private readonly ObservableValue<IReadOnlyList<KeybindingDefinition>> _observable = new();
    private readonly FileSystemWatcher? _watcher;
    private Timer? _reloadTimer;
    private List<KeybindingDefinition> _overrides = [];
    private DateTime _lastSavedAtUtc = DateTime.MinValue;
    private bool _disposed;

    public KeybindingService(ICommandRegistry commands, IFluenceStorageService storage)
    {
        _commands = commands;
        _path = storage.GetUserPath(KeybindingsFileName);
        LoadOverrides();

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            _watcher = new FileSystemWatcher(directory, KeybindingsFileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnKeybindingsFileChanged;
            _watcher.Created += OnKeybindingsFileChanged;
            _watcher.Renamed += OnKeybindingsFileChanged;
        }
    }

    public IReadOnlyList<KeybindingDefinition> GetKeybindings()
    {
        lock (_gate)
        {
            return BuildEffectiveKeybindings();
        }
    }

    public IReadOnlyList<KeybindingConflict> GetConflicts()
    {
        lock (_gate)
        {
            return BuildEffectiveKeybindings()
                .Where(binding => !string.IsNullOrWhiteSpace(binding.Key))
                .GroupBy(binding => $"{NormalizeScope(binding.Scope)}::{NormalizeKey(binding.Key)}", StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .SelectMany(group =>
                {
                    var items = group.ToArray();
                    return items.Skip(1).Select(item => new KeybindingConflict(
                        NormalizeScope(item.Scope),
                        item.Key,
                        items[0].Command,
                        item.Command));
                })
                .ToArray();
        }
    }

    public string? GetGesture(string commandId)
    {
        lock (_gate)
        {
            return BuildEffectiveKeybindings()
                .FirstOrDefault(binding => string.Equals(binding.Command, commandId, StringComparison.Ordinal))
                ?.Key;
        }
    }

    public void SetKeybinding(string commandId, string scope, string key)
    {
        lock (_gate)
        {
            var normalizedScope = NormalizeScope(scope);
            var existing = _overrides.FirstOrDefault(binding => string.Equals(binding.Command, commandId, StringComparison.Ordinal));
            if (existing is null)
            {
                _overrides.Add(new KeybindingDefinition
                {
                    Command = commandId,
                    Scope = normalizedScope,
                    Key = key.Trim(),
                });
            }
            else
            {
                existing.Scope = normalizedScope;
                existing.Key = key.Trim();
            }

            SaveOverrides();
            _observable.Publish(BuildEffectiveKeybindings());
        }
    }

    public void ResetKeybinding(string commandId)
    {
        lock (_gate)
        {
            _overrides.RemoveAll(binding => string.Equals(binding.Command, commandId, StringComparison.Ordinal));
            SaveOverrides();
            _observable.Publish(BuildEffectiveKeybindings());
        }
    }

    public async Task<bool> TryExecuteAsync(string scope, string key, CancellationToken cancellationToken = default)
    {
        IdeCommandDefinition? command = null;
        lock (_gate)
        {
            var normalizedScope = NormalizeScope(scope);
            var normalizedKey = NormalizeKey(key);
            var bindings = BuildEffectiveKeybindings();

            var binding = bindings.LastOrDefault(item =>
                    string.Equals(NormalizeScope(item.Scope), normalizedScope, StringComparison.Ordinal)
                    && string.Equals(NormalizeKey(item.Key), normalizedKey, StringComparison.Ordinal))
                ?? bindings.LastOrDefault(item =>
                    string.Equals(NormalizeScope(item.Scope), KeybindingScope.Global, StringComparison.Ordinal)
                    && string.Equals(NormalizeKey(item.Key), normalizedKey, StringComparison.Ordinal));

            if (binding is not null)
                command = _commands.Find(binding.Command);
        }

        if (command is null)
            return false;

        await _commands.ExecuteAsync(command.Id, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public IObservable<IReadOnlyList<KeybindingDefinition>> Watch()
    {
        lock (_gate)
        {
            _observable.Publish(BuildEffectiveKeybindings());
            return _observable;
        }
    }

    public void Reload()
    {
        lock (_gate)
        {
            LoadOverrides();
            _observable.Publish(BuildEffectiveKeybindings());
        }
    }

    public void ResetAll()
    {
        lock (_gate)
        {
            _overrides.Clear();
            SaveOverrides();
            _observable.Publish(BuildEffectiveKeybindings());
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

    private void OnKeybindingsFileChanged(object sender, FileSystemEventArgs e)
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

    private List<KeybindingDefinition> BuildEffectiveKeybindings()
    {
        var byCommand = new Dictionary<string, KeybindingDefinition>(StringComparer.Ordinal);

        foreach (var command in _commands.Commands)
        {
            byCommand[command.Id] = new KeybindingDefinition
            {
                Command = command.Id,
                Scope = command.Scope,
                Key = command.DefaultKey ?? string.Empty,
            };
        }

        foreach (var binding in _overrides)
        {
            if (string.IsNullOrWhiteSpace(binding.Command))
                continue;

            byCommand[binding.Command] = new KeybindingDefinition
            {
                Command = binding.Command,
                Scope = NormalizeScope(binding.Scope),
                Key = binding.Key.Trim(),
            };
        }

        return byCommand.Values
            .OrderBy(binding => binding.Scope, StringComparer.Ordinal)
            .ThenBy(binding => _commands.Find(binding.Command)?.Title ?? binding.Command, StringComparer.Ordinal)
            .ToList();
    }

    private void LoadOverrides()
    {
        if (!File.Exists(_path))
        {
            _overrides = [];
            return;
        }

        try
        {
            _overrides = JsonSerializer.Deserialize<List<KeybindingDefinition>>(File.ReadAllText(_path), JsonOptions) ?? [];
        }
        catch
        {
            _overrides = [];
        }
    }

    private void SaveOverrides()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_overrides, JsonOptions));
        _lastSavedAtUtc = DateTime.UtcNow;
    }

    private static string NormalizeScope(string? scope) =>
        string.IsNullOrWhiteSpace(scope) ? KeybindingScope.Global : scope.Trim().ToLowerInvariant();

    private static string NormalizeKey(string? key) =>
        Fluence.Core.Services.Keybindings.KeyGestureFormatter.Normalize(key);
}
