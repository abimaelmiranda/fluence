using System;
using System.Collections.Generic;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Models.Output;
using Fluence.Core.Abstractions.Dialogs;
using Fluence.Core.Abstractions.File;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Services.File;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.Editor.Abstractions;
using Fluence.Modules.Editor.Commands;
using Fluence.Modules.Editor.Json;
using Fluence.Modules.Editor.Services;
using Fluence.Modules.Editor.ViewModels;
using Fluence.Core.Events.Lsp;
using Fluence.Core.Events.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.Editor;

public sealed class Entrypoint : IModule
{
    private readonly List<IDisposable> _subscriptions = [];
    private EditorViewModel? _editorViewModel;

    public string Id => "Editor";

    public string DisplayName => "Editor";

    public const string ChannelId = "editor";

    public int StartupOrder => 400;

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<EditorViewModel>();
        services.AddSingleton<ITextFileService, TextFileService>();
        services.AddSingleton<EditorViewStateStore>();
        services.AddSingleton<ICommandHandler<OpenFileWorkspaceCommand>, OpenFileWorkspaceCommandHandler>();
        services.AddSingleton<ICommandHandler<SaveActiveDocumentCommand>, SaveActiveDocumentCommandHandler>();
    }

    public ModuleContributions GetContributions() =>
        new()
        {
            Panels =
            [
                new ShellPanelContribution(
                    ShellRegion.Main,
                    Id,
                    "Editor",
                    services => services.GetRequiredService<EditorViewModel>(),
                    PanelVisibilityRule.Always),
            ],
            OutputChannel = new OutputChannelDescriptor(ChannelId, "Editor"),
        };

    public Task InitializeAsync(IModuleHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scheduler = host.Services.GetRequiredService<ITaskScheduler>();

        host.Services.GetRequiredService<ILocalizationService>()
            .Register(new ResourceManager("Fluence.Modules.Editor.Resources.Strings", typeof(Entrypoint).Assembly));

        host.Services.GetRequiredService<ISettingsRegistry>()
            .Register(EditorSettingsJsonContext.Default.EditorSettings);
        host.Services.GetRequiredService<ISettingsService>()
            .Get<EditorSettings>();

        _editorViewModel = host.Services.GetRequiredService<EditorViewModel>();

        _subscriptions.Add(host.Events.SubscribeSync<OpenFileRequestedEvent>(e =>
            scheduler.Schedule("editor.open", TaskPriority.Interactive,
                ct => OpenFileAsync(host, e.Path, ct),
                correlationId: e.Path)));

        _subscriptions.Add(host.Events.SubscribeSync<OpenFileAtLocationRequestedEvent>(e =>
            scheduler.Schedule("editor.open-location", TaskPriority.Interactive,
                ct => OpenFileAtLocationAsync(host, e.Path, e.Line, e.Character, ct),
                correlationId: $"{e.Path}:{e.Line}:{e.Character}")));

        _subscriptions.Add(host.Events.SubscribeSync<SaveActiveDocumentRequestedEvent>(_ =>
            scheduler.Schedule("editor.save", TaskPriority.Critical,
                ct => SaveActiveDocumentAsync(host, ct))));

        host.SetModuleState(Id, ModuleState.Active);
        return Task.CompletedTask;
    }

    private static async Task OpenFileAtLocationAsync(
        IModuleHost host,
        string path,
        int line,
        int character,
        CancellationToken ct)
    {
        await OpenFileAsync(host, path, ct);
        host.Events.Publish(new NavigationResolvedEvent(path, line, character));
    }

    private static async Task OpenFileAsync(IModuleHost host, string path, CancellationToken ct)
    {
        try
        {
            var handler = host.Services.GetRequiredService<ICommandHandler<OpenFileWorkspaceCommand>>();
            await handler.HandleAsync(new OpenFileWorkspaceCommand(path), ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (OpenFileFailureNotification.TryShow(
            host.Services.GetRequiredService<IUserNotificationService>(),
            path,
            ex))
        {
        }
    }

    private static async Task SaveActiveDocumentAsync(IModuleHost host, CancellationToken ct)
    {
        var editor = host.Services.GetRequiredService<EditorViewModel>();
        await editor.SaveManuallyAsync(ct);
    }

    public ValueTask DisposeAsync()
    {
        foreach (var subscription in _subscriptions)
            subscription.Dispose();
        _subscriptions.Clear();
        _editorViewModel?.Dispose();
        _editorViewModel = null;
        return ValueTask.CompletedTask;
    }
}
