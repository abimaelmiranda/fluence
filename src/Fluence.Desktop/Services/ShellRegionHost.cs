using System;
using System.Collections.Generic;
using System.Linq;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Models.Workbench;
using Fluence.Core.Events.Ui;

namespace Fluence.Desktop.Services;

public sealed class ShellRegionHost : IShellRegionHost, IDisposable
{
    private readonly IServiceProvider _services;
    private readonly IWorkspaceContext _workspace;
    private readonly IDisposable _activitySubscription;
    private readonly IDisposable _bottomBarSubscription;
    private readonly List<ShellPanelContribution> _panels = [];
    private string? _activeActivityTabId;
    private string _activeBottomBarTabId = BottomBarTabIds.Terminal;
    private ShellRegionContent? _mainContent;
    private ShellRegionContent? _sidebarContent;
    private ShellRegionContent? _bottomBarContent;

    public ShellRegionHost(
        IServiceProvider services,
        IWorkspaceContext workspace,
        IShellEventBus events)
    {
        _services = services;
        _workspace = workspace;
        _workspace.Changed += OnWorkspaceChanged;
        _activitySubscription = events.SubscribeSync<ActivityBarTabChangedEvent>(e =>
        {
            _activeActivityTabId = e.TabId;
            Refresh();
        });
        _bottomBarSubscription = events.SubscribeSync<BottomBarTabChangedEvent>(e =>
        {
            _activeBottomBarTabId = e.TabId;
            Refresh();
        });
    }

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

    public void RegisterPanels(IEnumerable<ShellPanelContribution> panels)
    {
        _panels.Clear();
        _panels.AddRange(panels);
        Refresh();
    }

    public void Refresh()
    {
        ApplyRegion(ShellRegion.Main);
        ApplyRegion(ShellRegion.Sidebar);
        ApplyRegion(ShellRegion.BottomBar);
    }

    public void Expand(ShellRegion region)
    {
        RegionExpanded?.Invoke(this, new ShellRegionExpandedEventArgs(region));
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e) => Refresh();

    private void ApplyRegion(ShellRegion region)
    {
        var content = ResolveContent(region);
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

    private ShellRegionContent? ResolveContent(ShellRegion region)
    {
        var contribution = _panels
            .Where(panel => panel.Region == region && IsVisible(panel.Visibility))
            .OrderBy(panel => panel.Order)
            .LastOrDefault();

        if (contribution is not null)
        {
            return new ShellRegionContent(
                contribution.Region,
                contribution.ContentId,
                contribution.Title,
                contribution.ResolveViewModel(_services));
        }

        return null;
    }

    private bool IsVisible(PanelVisibilityRule visibility)
    {
        return visibility.Kind switch
        {
            PanelVisibilityKind.Always => true,
            PanelVisibilityKind.WorkspaceMode => visibility.WorkspaceMode == _workspace.Current.NavigationMode,
            PanelVisibilityKind.ActivityTab => string.Equals(
                visibility.ActivityTabId,
                _activeActivityTabId,
                StringComparison.Ordinal),
            PanelVisibilityKind.BottomBarTab => string.Equals(
                visibility.ActivityTabId,
                _activeBottomBarTabId,
                StringComparison.Ordinal),
            PanelVisibilityKind.Custom => visibility.Predicate?.Invoke(
                _workspace.Current,
                _activeActivityTabId,
                _services) == true,
            _ => false,
        };
    }

    public void Dispose()
    {
        _workspace.Changed -= OnWorkspaceChanged;
        _activitySubscription.Dispose();
        _bottomBarSubscription.Dispose();
    }
}
