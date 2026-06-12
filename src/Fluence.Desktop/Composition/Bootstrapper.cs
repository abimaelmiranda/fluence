using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fluence.Application.DotnetCli;
using Fluence.Application.Workspace;
using Fluence.Core.Commands;
using Fluence.Core.Infrastructure;
using Fluence.Core.Modules;
using Fluence.Core.Workspace;
using Fluence.Desktop.Services;
using Fluence.Desktop.ViewModels;
using Fluence.Infrastructure;
using Fluence.Infrastructure.Pty;
using Fluence.Modules.DotnetCli;
using Fluence.Modules.Editor;
using Fluence.Modules.FileExplorer;
using Fluence.Modules.SolutionView;
using Fluence.Modules.Terminal;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Desktop.Composition;

internal static class Bootstrapper
{
    public static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IWorkspaceContext, WorkspaceContext>();
        services.AddSingleton<IModuleHost, ModuleHost>();
        services.AddSingleton<IWorkspaceDialogService, AvaloniaWorkspaceDialogService>();
        services.AddSingleton<IProjectReferenceDialogService, AvaloniaProjectReferenceDialogService>();
        services.AddSingleton<AvaloniaUserNotificationService>();
        services.AddSingleton<IUserNotificationService>(provider => provider.GetRequiredService<AvaloniaUserNotificationService>());
        services.AddSingleton<ITextFileService, TextFileService>();
        services.AddSingleton<ISolutionWorkspaceLoader, BuildalyzerSolutionWorkspaceLoader>();
        services.AddSingleton<IProjectReferenceService, ProjectReferenceService>();
        services.AddSingleton<IProcessHost, ProcessHost>();
        services.AddSingleton<IPtyHost>(_ =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? new MacOsPtyHost()
                : new WindowsPtyHost());
        services.AddSingleton<ITerminalService, TerminalService>();
        services.AddSingleton<ICommandHandler<OpenFileWorkspaceCommand>, OpenFileWorkspaceCommandHandler>();
        services.AddSingleton<ICommandHandler<OpenFolderWorkspaceCommand>, OpenFolderWorkspaceCommandHandler>();
        services.AddSingleton<ICommandHandler<OpenSolutionWorkspaceCommand>, OpenSolutionWorkspaceCommandHandler>();
        services.AddSingleton<ICommandHandler<SaveActiveDocumentCommand>, SaveActiveDocumentCommandHandler>();
        services.AddSingleton<ICommandHandler<AddProjectReferencesCommand>, AddProjectReferencesCommandHandler>();
        services.AddSingleton<ICommandHandler<RemoveProjectReferenceCommand>, RemoveProjectReferenceCommandHandler>();
        services.AddSingleton<ICommandHandler<SetStartupProjectCommand>, SetStartupProjectCommandHandler>();
        services.AddSingleton<ICommandHandler<BuildWorkspaceCommand>, BuildWorkspaceCommandHandler>();
        services.AddSingleton<ICommandHandler<RunProjectCommand>, RunProjectCommandHandler>();
        services.AddSingleton<ICommandHandler<TestWorkspaceCommand>, TestWorkspaceCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreWorkspaceCommand>, RestoreWorkspaceCommandHandler>();
        services.AddSingleton<ICommandHandler<CleanWorkspaceCommand>, CleanWorkspaceCommandHandler>();
        services.AddSingleton<ICommandHandler<BuildProjectCommand>, BuildProjectCommandHandler>();
        services.AddSingleton<ICommandHandler<RunSpecificProjectCommand>, RunSpecificProjectCommandHandler>();
        services.AddSingleton<ICommandHandler<TestProjectCommand>, TestProjectCommandHandler>();
        services.AddSingleton<ICommandHandler<RestoreProjectCommand>, RestoreProjectCommandHandler>();
        services.AddSingleton<ICommandHandler<CleanProjectCommand>, CleanProjectCommandHandler>();
        services.AddSingleton<WelcomeViewModel>();
        services.AddSingleton<EditorViewModel>();
        services.AddSingleton<FileExplorerViewModel>();
        services.AddSingleton<SolutionViewModel>();
        services.AddSingleton<TerminalViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        var modules = new IIdeModule[]
        {
            new FileExplorerModule(),
            new SolutionViewModule(),
            new EditorModule(),
            new TerminalModule(),
            new DotnetCliModule(),
        };

        foreach (var module in modules)
        {
            module.Register(services);
        }

        services.AddSingleton<IReadOnlyList<IIdeModule>>(modules);

        return services.BuildServiceProvider();
    }

    public static void InitializeModules(IServiceProvider serviceProvider)
    {
        var host = serviceProvider.GetRequiredService<IModuleHost>();
        var modules = serviceProvider.GetRequiredService<IReadOnlyList<IIdeModule>>();

        foreach (var module in modules)
        {
            try
            {
                module.Initialize(host);
            }
            catch
            {
                host.SetModuleState(module.Name, ModuleState.Faulted);
            }
        }
    }
}
