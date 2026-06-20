using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.File;
using Fluence.Core.Abstractions.Keybindings;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Events.Document;
using Fluence.Core.Models.Keybindings;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Modules.XamlViewer.Abstractions;
using Fluence.Modules.XamlViewer.Services;
using Fluence.Modules.XamlViewer.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.XamlViewer;

public sealed class Entrypoint : IModule
{
    private readonly List<IDisposable> _subscriptions = [];
    private XamlViewerViewModel? _viewModel;
    private string? _currentPreviewPath;

    public string Id => "XamlViewer";
    public string DisplayName => "XAML Viewer";
    public int StartupOrder => 450;

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IXamlPreviewService, XamlPreviewService>();
    }

    public Task InitializeAsync(IModuleHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var registry = host.Services.GetRequiredService<IFileViewerRegistry>();
        registry.Register(".axaml");
        registry.Register(".xaml");

        var scheduler = host.Services.GetRequiredService<ITaskScheduler>();
        var commands = host.Services.GetRequiredService<ICommandRegistry>();
        var workspace = host.Services.GetRequiredService<IWorkspaceContext>();
        var previewService = host.Services.GetRequiredService<IXamlPreviewService>();
        var dispatcher = host.Services.GetRequiredService<IUiDispatcher>();
        var events = host.Services.GetRequiredService<IShellEventBus>();

        commands.Register(new IdeCommandDefinition(
            CommandIds.ToggleXamlPreview,
            "Toggle XAML Preview",
            KeybindingScope.Global,
            "Alt+F7",
            ct =>
            {
                scheduler.Schedule(
                    "xamlViewer.toggle",
                    TaskPriority.Interactive,
                    innerCt => ToggleAsync(workspace, previewService, dispatcher, innerCt));
                return Task.CompletedTask;
            }));

        _subscriptions.Add(events.SubscribeSync<DocumentChangedEvent>(e =>
        {
            if (!IsXamlFile(e.FilePath)) return;
            if (_viewModel is null || _currentPreviewPath != e.FilePath) return;
            if (!_viewModel.IsHotReloadEnabled) return;

            scheduler.ScheduleLatest(
                "xamlViewer.hotReload",
                TaskPriority.Background,
                TimeSpan.FromMilliseconds(500),
                ct => RefreshAsync(e.Content, ct));
        }));

        host.SetModuleState(Id, ModuleState.Active);
        return Task.CompletedTask;
    }

    private async Task ToggleAsync(
        IWorkspaceContext workspace,
        IXamlPreviewService previewService,
        IUiDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var isPreviewOpen = workspace.Current.TabSession.Documents
            .Any(d => string.Equals(d.Path, ToolTabIds.XamlPreview, StringComparison.Ordinal));

        if (isPreviewOpen)
        {
            var sourceFile = _currentPreviewPath;
            workspace.CloseDocument(ToolTabIds.XamlPreview);
            if (sourceFile != null)
                workspace.ActivateDocument(sourceFile);
            _currentPreviewPath = null;
            return;
        }

        var activeDocument = workspace.Current.TabSession.ActiveDocument;
        if (activeDocument is null || !IsXamlFile(activeDocument.Path))
            return;

        var filePath = activeDocument.Path;
        var vm = GetOrCreateViewModel(previewService, dispatcher);
        _currentPreviewPath = filePath;
        workspace.OpenToolTab(ToolTabIds.XamlPreview, Path.GetFileName(filePath), vm);
        await vm.LoadAsync(filePath, cancellationToken);
    }

    private async Task RefreshAsync(string xamlContent, CancellationToken ct)
    {
        if (_viewModel is null) return;
        await _viewModel.LoadFromContentAsync(xamlContent, ct);
    }

    private XamlViewerViewModel GetOrCreateViewModel(IXamlPreviewService previewService, IUiDispatcher dispatcher)
    {
        _viewModel ??= new XamlViewerViewModel(previewService, dispatcher);
        return _viewModel;
    }

    private static bool IsXamlFile(string path)
    {
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".axaml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".xaml", StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var sub in _subscriptions)
            sub.Dispose();
        _subscriptions.Clear();
        _viewModel?.Dispose();
        _viewModel = null;
        _currentPreviewPath = null;
        return ValueTask.CompletedTask;
    }
}
