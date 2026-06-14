using System;
using System.Threading.Tasks;
using Fluence.Core.Commands;
using Fluence.Core.Modules.Abstractions;
using Fluence.Core.Modules;
using Fluence.Core.Ports;
using Fluence.Core.Workspace;
using Fluence.Modules.Editor.Abstractions;
using Fluence.Modules.Editor.Commands;
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
        host.ShellRegions.SetContent(
            ShellRegion.Main,
            Name,
            "Editor",
            host.Services.GetRequiredService<EditorViewModel>());
        host.Events.Subscribe<OpenFileRequestedEvent>(e => { _ = OpenFileAsync(host, e.Path); });
        host.Events.Subscribe<SaveActiveDocumentRequestedEvent>(_event => { _ = SaveActiveDocumentAsync(host); });
        host.SetModuleState(Name, ModuleState.Active);
    }

    private static async Task OpenFileAsync(IModuleHost host, string path)
    {
        try
        {
            var handler = host.Services.GetRequiredService<ICommandHandler<OpenFileWorkspaceCommand>>();
            await handler.HandleAsync(new OpenFileWorkspaceCommand(path));
        }
        catch (Exception ex) when (OpenFileFailureNotification.TryShow(
            host.Services.GetRequiredService<IUserNotificationService>(),
            path,
            ex))
        {
        }
    }

    private static async Task SaveActiveDocumentAsync(IModuleHost host)
    {
        var handler = host.Services.GetRequiredService<ICommandHandler<SaveActiveDocumentCommand>>();
        await handler.HandleAsync(new SaveActiveDocumentCommand());
    }
}