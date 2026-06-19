using System;
using System.IO;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Modules.SourceControl.Abstractions;
using Fluence.Modules.SourceControl.Infrastructure;
using Fluence.Modules.SourceControl.Services;
using Fluence.Modules.SourceControl.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.SourceControl;

public sealed class Entrypoint : IModule
{
    private IWorkspaceContext? _workspace;
    private EventHandler? _workspaceChanged;
    private string? _lastWorkspaceRoot;
    private bool _hasInitializedWorkspaceRoot;

    public string Id => "SourceControl";

    public string DisplayName => "Source Control";

    public int StartupOrder => 1200;

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IGitService, GitCliService>();
        services.AddSingleton<ISourceControlDialogService, AvaloniaSourceControlDialogService>();
        services.AddSingleton<SourceControlViewModel>();
    }

    public ModuleContributions GetContributions() =>
        new()
        {
            Panels =
            [
                new ShellPanelContribution(
                    ShellRegion.Sidebar,
                    Id,
                    "Source Control",
                    services => services.GetRequiredService<SourceControlViewModel>(),
                    PanelVisibilityRule.ForActivityTab("SourceControl")),
            ],
        };

    public Task InitializeAsync(IModuleHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        host.Services.GetRequiredService<ILocalizationService>()
            .Register(new ResourceManager("Fluence.Modules.SourceControl.Resources.Strings", typeof(Entrypoint).Assembly));

        _workspace = host.Workspace;
        _workspaceChanged = (_, _) =>
        {
            var root = GetWorkspaceRoot(host.Workspace);
            if (!HasWorkspaceRootChanged(root))
            {
                return;
            }

            _hasInitializedWorkspaceRoot = true;
            _lastWorkspaceRoot = root;
            host.Services.GetRequiredService<SourceControlViewModel>().Initialize(root);
        };
        host.Workspace.Changed += _workspaceChanged;

        host.SetModuleState(Id, ModuleState.Active);
        return Task.CompletedTask;
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
        if (_workspace is not null && _workspaceChanged is not null)
            _workspace.Changed -= _workspaceChanged;
        _workspace = null;
        _workspaceChanged = null;
        _lastWorkspaceRoot = null;
        _hasInitializedWorkspaceRoot = false;
        return ValueTask.CompletedTask;
    }
}
