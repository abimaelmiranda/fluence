using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Services.Debugging;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Models.Infrastructure;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Services.Modules;
using Fluence.Core.Abstractions.Dialogs;
using Fluence.Core.Abstractions.File;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Abstractions.Storage;
using Fluence.Core.Services.File;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Desktop.Services;
using Fluence.Desktop.ViewModels;
using Fluence.Desktop.Views;
using Fluence.Infrastructure;
using Fluence.Infrastructure.Pty;
using Fluence.Infrastructure.Protocols.Dap;
using Fluence.Infrastructure.Protocols.Lsp;
using Fluence.Core.Abstractions.LanguageServer;
using FileExplorerEntrypoint = Fluence.Modules.FileExplorer.Entrypoint;
using SolutionViewEntrypoint = Fluence.Modules.SolutionView.Entrypoint;
using EditorEntrypoint = Fluence.Modules.Editor.Entrypoint;
using NuGetExplorerEntrypoint = Fluence.Modules.NuGetExplorer.Entrypoint;
using TerminalEntrypoint = Fluence.Modules.Terminal.Entrypoint;
using DotnetCliEntrypoint = Fluence.Modules.DotnetCli.Entrypoint;
using DebuggerSetupEntrypoint = Fluence.Modules.DebuggerSetup.Entrypoint;
using DebugEntrypoint = Fluence.Modules.Debug.Entrypoint;
using SourceControlEntrypoint = Fluence.Modules.SourceControl.Entrypoint;
using LspSetupEntrypoint = Fluence.Modules.LspSetup.Entrypoint;
using LanguageServerEntrypoint = Fluence.Modules.LanguageServer.Entrypoint;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Desktop.Composition;

internal static class Bootstrapper
{
    public static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        var viewRegistry = new ViewRegistry();
        viewRegistry.Register<WelcomeViewModel, WelcomeView>();
        services.AddSingleton<IViewRegistry>(viewRegistry);
        services.AddSingleton<IShellEventBus>(
            _ => new ShellEventBus(a => Avalonia.Threading.Dispatcher.UIThread.Post(a, Avalonia.Threading.DispatcherPriority.Background)));
        services.AddSingleton<ShellRegionHost>();
        services.AddSingleton<IShellRegionHost>(provider => provider.GetRequiredService<ShellRegionHost>());
        services.AddSingleton<IWorkspaceContext, WorkspaceContext>();
        services.AddSingleton<IModuleHost, ModuleHost>();
        services.AddSingleton<IWorkspaceDialogService, AvaloniaWorkspaceDialogService>();
        services.AddSingleton<ILaunchSetupDialogService, AvaloniaLaunchSetupDialogService>();
        services.AddSingleton<AvaloniaUserNotificationService>();
        services.AddSingleton<IUserNotificationService>(provider => provider.GetRequiredService<AvaloniaUserNotificationService>());
        services.AddSingleton<IFileClipboardService, FileClipboardService>();
        services.AddSingleton<IFileOperationDialogService, AvaloniaFileOperationDialogService>();
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<IFluenceStorageService, FluenceStorageService>();
        services.AddSingleton<IProcessHost, ProcessHost>();
        services.AddSingleton<ILaunchSettingsService, LaunchSettingsService>();
        services.AddSingleton<ILaunchSettingsCoordinator, LaunchSettingsCoordinator>();
        services.AddSingleton<NetcoredbgToolService>();
        services.AddSingleton<IDebugAdapterClientFactory, DapDebugAdapterClientFactory>();
        services.AddSingleton<IDebuggerProvisioningService, DebuggerProvisioningService>();
        services.AddSingleton<ILspProvisioningService, OmniSharpProvisioningService>();
        services.AddSingleton<IPtyHost>(_ =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? new PortaMacOsPtyHost()
                : new WindowsPtyHost());
        services.AddSingleton<ITerminalService, TerminalService>();
        services.AddSingleton<IRecentProjectsService, RecentProjectsService>();
        services.AddSingleton<IWorkspaceSnapshotService, WorkspaceSnapshotService>();
        services.AddSingleton<WorkspaceSnapshotCoordinator>();
        services.AddSingleton<WelcomeViewModel>();
        services.AddSingleton<ActivityBarViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        var modules = new IModule[]
        {
            new FileExplorerEntrypoint(),
            new SolutionViewEntrypoint(),
            new EditorEntrypoint(),
            new NuGetExplorerEntrypoint(),
            new TerminalEntrypoint(),
            new DotnetCliEntrypoint(),
            new LspSetupEntrypoint(),
            new LanguageServerEntrypoint(),
            new DebuggerSetupEntrypoint(),
            new DebugEntrypoint(),
            new SourceControlEntrypoint(),
        };

        foreach (var module in modules)
        {
            module.Register(services);
        }

        services.AddSingleton<IReadOnlyList<IModule>>(modules);

        return services.BuildServiceProvider();
    }

    public static void InitializeModules(IServiceProvider serviceProvider)
    {
        var host = serviceProvider.GetRequiredService<IModuleHost>();
        var modules = serviceProvider.GetRequiredService<IReadOnlyList<IModule>>();

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
