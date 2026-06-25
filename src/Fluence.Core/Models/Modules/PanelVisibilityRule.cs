using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Core.Models.Modules;

public sealed record PanelVisibilityRule(
    PanelVisibilityKind Kind,
    WorkspaceMode? WorkspaceMode = null,
    string? ActivityTabId = null,
    Func<Fluence.Core.Models.Workspace.Workspace, string?, IServiceProvider, bool>? Predicate = null)
{
    public static PanelVisibilityRule Always { get; } = new(PanelVisibilityKind.Always);

    public static PanelVisibilityRule ForWorkspaceMode(WorkspaceMode mode) =>
        new(PanelVisibilityKind.WorkspaceMode, WorkspaceMode: mode);

    public static PanelVisibilityRule ForActivityTab(string tabId) =>
        new(PanelVisibilityKind.ActivityTab, ActivityTabId: tabId);

    public static PanelVisibilityRule ForBottomBarTab(string tabId) =>
        new(PanelVisibilityKind.BottomBarTab, ActivityTabId: tabId);

    public static PanelVisibilityRule Custom(Func<Fluence.Core.Models.Workspace.Workspace, string?, IServiceProvider, bool> predicate) =>
        new(PanelVisibilityKind.Custom, Predicate: predicate);
}
