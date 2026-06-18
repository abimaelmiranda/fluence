using System;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Services.Modules;
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
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.Editor;

public sealed class Entrypoint : IModule
{
    public string Name => "Editor";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<EditorViewModel>();
        services.AddSingleton<ITextFileService, TextFileService>();
        services.AddSingleton<ICommandHandler<OpenFileWorkspaceCommand>, OpenFileWorkspaceCommandHandler>();
        services.AddSingleton<ICommandHandler<SaveActiveDocumentCommand>, SaveActiveDocumentCommandHandler>();
    }

    public void Initialize(IModuleHost host)
    {
        var scheduler = host.Services.GetRequiredService<ITaskScheduler>();

        host.Services.GetRequiredService<ISettingsRegistry>()
            .Register(EditorSettingsJsonContext.Default.EditorSettings);
        host.Services.GetRequiredService<ISettingsService>()
            .Get<EditorSettings>();

        host.ShellRegions.SetContent(
            ShellRegion.Main,
            Name,
            "Editor",
            host.Services.GetRequiredService<EditorViewModel>());

        host.Events.SubscribeSync<OpenFileRequestedEvent>(e =>
            scheduler.Schedule("editor.open", TaskPriority.Interactive,
                ct => OpenFileAsync(host, e.Path, ct),
                correlationId: e.Path));

        host.Events.SubscribeSync<SaveActiveDocumentRequestedEvent>(_ =>
            scheduler.Schedule("editor.save", TaskPriority.Critical,
                ct => SaveActiveDocumentAsync(host, ct)));

        host.SetModuleState(Name, ModuleState.Active);
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
}
