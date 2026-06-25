using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Ui;

public sealed record BottomBarTabChangedEvent(string TabId) : IShellEvent;
