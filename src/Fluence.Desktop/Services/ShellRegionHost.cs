using System;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Services.Modules;

namespace Fluence.Desktop.Services;

public sealed class ShellRegionHost : IShellRegionHost
{
    private ShellRegionContent? _mainContent;
    private ShellRegionContent? _sidebarContent;
    private ShellRegionContent? _bottomBarContent;

    public event EventHandler? Changed;

    public event EventHandler<ShellRegionExpandedEventArgs>? RegionExpanded;

    public ShellRegionContent? MainContent
    {
        get => _mainContent;
        private set
        {
            if (Equals(_mainContent, value)) return;
            _mainContent = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public ShellRegionContent? SidebarContent
    {
        get => _sidebarContent;
        private set
        {
            if (Equals(_sidebarContent, value)) return;
            _sidebarContent = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public ShellRegionContent? BottomBarContent
    {
        get => _bottomBarContent;
        private set
        {
            if (Equals(_bottomBarContent, value)) return;
            _bottomBarContent = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetContent(ShellRegion region, string contentId, string title, object viewModel)
    {
        var content = new ShellRegionContent(region, contentId, title, viewModel);
        switch (region)
        {
            case ShellRegion.Main:
                MainContent = content;
                break;
            case ShellRegion.Sidebar:
                SidebarContent = content;
                break;
            case ShellRegion.BottomBar:
                BottomBarContent = content;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(region), region, null);
        }
    }

    public void ClearContent(ShellRegion region, string contentId)
    {
        switch (region)
        {
            case ShellRegion.Main when MainContent?.ContentId == contentId:
                MainContent = null;
                break;
            case ShellRegion.Sidebar when SidebarContent?.ContentId == contentId:
                SidebarContent = null;
                break;
            case ShellRegion.BottomBar when BottomBarContent?.ContentId == contentId:
                BottomBarContent = null;
                break;
        }
    }

    public void Expand(ShellRegion region)
    {
        RegionExpanded?.Invoke(this, new ShellRegionExpandedEventArgs(region));
    }
}
