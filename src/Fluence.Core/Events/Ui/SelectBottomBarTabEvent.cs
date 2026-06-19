using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Ui;

public sealed record SelectBottomBarTabEvent(string TabId) : IShellEvent;
