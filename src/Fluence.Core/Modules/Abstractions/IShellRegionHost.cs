using System;

namespace Fluence.Core.Modules;

public interface IShellRegionHost
{
    event EventHandler? Changed;

    event EventHandler<ShellRegionExpandedEventArgs>? RegionExpanded;

    ShellRegionContent? MainContent { get; }

    ShellRegionContent? SidebarContent { get; }

    ShellRegionContent? BottomBarContent { get; }

    void SetContent(ShellRegion region, string contentId, string title, object viewModel);

    void ClearContent(ShellRegion region, string contentId);

    void Expand(ShellRegion region);
}

public sealed class ShellRegionExpandedEventArgs(ShellRegion region) : EventArgs
{
    public ShellRegion Region { get; } = region;
}
