using System;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;

namespace Fluence.Core.Abstractions.Modules;

public interface IShellRegionHost
{
    event EventHandler? Changed;

    event EventHandler<ShellRegionExpandedEventArgs>? RegionExpanded;

    ShellRegionContent? MainContent { get; }

    ShellRegionContent? SidebarContent { get; }

    ShellRegionContent? BottomBarContent { get; }

    void RegisterPanels(IEnumerable<ShellPanelContribution> panels);

    void Refresh();

    void Expand(ShellRegion region);
}

public sealed class ShellRegionExpandedEventArgs(ShellRegion region) : EventArgs
{
    public ShellRegion Region { get; } = region;
}
