using System;
using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Models.Keybindings;

public sealed record IdeCommandDefinition(
    string Id,
    string Title,
    string Scope,
    string? DefaultKey,
    Func<CancellationToken, Task>? ExecuteAsync = null);
