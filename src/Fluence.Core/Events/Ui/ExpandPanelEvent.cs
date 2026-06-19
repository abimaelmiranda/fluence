using Fluence.Core.Abstractions.Modules;

namespace Fluence.Core.Events.Ui;

public sealed class ExpandPanelEvent(string panelId) : IShellEvent
{
    public string PanelId { get; } = panelId;
}
