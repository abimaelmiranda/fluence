using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Models.Keybindings;

namespace Fluence.Core.Abstractions.Keybindings;

public interface ICommandRegistry
{
    event EventHandler? Changed;

    void Register(IdeCommandDefinition command);

    IdeCommandDefinition? Find(string commandId);

    IReadOnlyList<IdeCommandDefinition> Commands { get; }

    Task ExecuteAsync(string commandId, CancellationToken cancellationToken = default);
}
