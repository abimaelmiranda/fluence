using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Ui;

public sealed record ActivityBarTabSelectRequestedEvent(string? TabId) : IShellEvent;
