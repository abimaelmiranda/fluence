using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Keybindings;
using Fluence.Core.Models.Keybindings;

namespace Fluence.Core.Services.Keybindings;

public sealed class CommandRegistry : ICommandRegistry
{
    private readonly Dictionary<string, IdeCommandDefinition> _commands = new(StringComparer.Ordinal);

    public event EventHandler? Changed;

    public IReadOnlyList<IdeCommandDefinition> Commands => _commands.Values
        .OrderBy(command => command.Scope, StringComparer.Ordinal)
        .ThenBy(command => command.Title, StringComparer.Ordinal)
        .ToArray();

    public void Register(IdeCommandDefinition command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Id);
        _commands[command.Id] = command;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IdeCommandDefinition? Find(string commandId) =>
        _commands.TryGetValue(commandId, out var command) ? command : null;

    public Task ExecuteAsync(string commandId, CancellationToken cancellationToken = default)
    {
        if (!_commands.TryGetValue(commandId, out var command) || command.ExecuteAsync is null)
            return Task.CompletedTask;

        return command.ExecuteAsync(cancellationToken);
    }
}
