using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Models.Keybindings;

namespace Fluence.Core.Abstractions.Keybindings;

public interface IKeybindingService
{
    IReadOnlyList<KeybindingDefinition> GetKeybindings();

    IReadOnlyList<KeybindingConflict> GetConflicts();

    string? GetGesture(string commandId);

    void SetKeybinding(string commandId, string scope, string key);

    void ResetKeybinding(string commandId);

    void Reload();

    void ResetAll();

    Task<bool> TryExecuteAsync(string scope, string key, CancellationToken cancellationToken = default);

    IObservable<IReadOnlyList<KeybindingDefinition>> Watch();
}
