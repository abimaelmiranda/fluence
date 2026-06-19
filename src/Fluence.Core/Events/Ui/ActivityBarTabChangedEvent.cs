using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Ui;

public sealed record ActivityBarTabChangedEvent(string? TabId) : IShellEvent;
