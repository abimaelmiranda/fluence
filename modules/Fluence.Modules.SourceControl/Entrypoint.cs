using System;
using System.IO;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Modules;
using Fluence.Modules.SourceControl.Abstractions;
using Fluence.Modules.SourceControl.Infrastructure;
using Fluence.Modules.SourceControl.Services;
using Fluence.Modules.SourceControl.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.SourceControl;

public sealed class Entrypoint : IModule
{
    private string? _activeTabId;
    private IDisposable? _activitySubscription;
    private IWorkspaceContext? _workspace;
    private EventHandler? _workspaceChanged;
    private string? _lastWorkspaceRoot;
    private bool _hasInitializedWorkspaceRoot;

    public string Name => "SourceControl";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IGitService, GitCliService>();
        services.AddSingleton<ISourceControlDialogService, AvaloniaSourceControlDialogService>();
        services.AddSingleton<SourceControlViewModel>();
    }

    public void Initialize(IModuleHost host)
    {
        _activitySubscription = host.Events.SubscribeSync<ActivityBarTabChangedEvent>(e =>
        {
            _activeTabId = e.TabId;
            UpdateSidebar(host);
        });

        _workspace = host.Workspace;
        _workspaceChanged = (_, _) =>
        {
            var root = GetWorkspaceRoot(host.Workspace);
            if (!HasWorkspaceRootChanged(root))
            {
                UpdateSidebar(host);
                return;
            }

            _hasInitializedWorkspaceRoot = true;
            _lastWorkspaceRoot = root;
            host.Services.GetRequiredService<SourceControlViewModel>().Initialize(root);
            UpdateSidebar(host);
        };
        host.Workspace.Changed += _workspaceChanged;

        host.SetModuleState(Name, ModuleState.Active);
    }

    private void UpdateSidebar(IModuleHost host)
    {
        if (_activeTabId == "SourceControl")
        {
            host.ShellRegions.SetContent(
                ShellRegion.Sidebar,
                "SourceControl",
                "Source Control",
                host.Services.GetRequiredService<SourceControlViewModel>());
            return;
        }

        host.ShellRegions.ClearContent(ShellRegion.Sidebar, "SourceControl");
    }

    private static string? GetWorkspaceRoot(IWorkspaceContext workspace)
    {
        var current = workspace.Current;
        return current.CurrentFolderPath
            ?? (current.CurrentSolutionPath is not null
                ? Path.GetDirectoryName(current.CurrentSolutionPath)
                : null);
    }

    private bool HasWorkspaceRootChanged(string? root) =>
        !_hasInitializedWorkspaceRoot ||
        !string.Equals(_lastWorkspaceRoot, root, StringComparison.OrdinalIgnoreCase);

    public ValueTask DisposeAsync()
    {
        _activitySubscription?.Dispose();
        if (_workspace is not null && _workspaceChanged is not null)
            _workspace.Changed -= _workspaceChanged;
        _activitySubscription = null;
        _workspace = null;
        _workspaceChanged = null;
        _lastWorkspaceRoot = null;
        _hasInitializedWorkspaceRoot = false;
        return ValueTask.CompletedTask;
    }
}
