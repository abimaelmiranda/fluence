using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Fluence.Core.Debug;
using Fluence.Core.Infrastructure;
using Fluence.Core.Modules;
using Fluence.Core.Modules.Abstractions;
using Fluence.Core.Ports;
using Fluence.Core.Workspace;
using Fluence.Desktop.Services;
using Fluence.Desktop.ViewModels;
using Fluence.Desktop.Views;
using Fluence.Infrastructure;
using Fluence.Infrastructure.Pty;
using Fluence.Infrastructure.Protocols.Dap;
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
        services.AddSingleton<IShellEventBus, ShellEventBus>();
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
        services.AddSingleton<IProcessHost, ProcessHost>();
        services.AddSingleton<ILaunchSettingsService, LaunchSettingsService>();
        services.AddSingleton<ILaunchSettingsCoordinator, LaunchSettingsCoordinator>();
        services.AddSingleton<NetcoredbgToolService>();
        services.AddSingleton<IDebugAdapterClientFactory, DapDebugAdapterClientFactory>();
        services.AddSingleton<IDebuggerProvisioningService, DebuggerProvisioningService>();
        services.AddSingleton<IPtyHost>(_ =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? new MacOsPtyHost()
                : new WindowsPtyHost());
        services.AddSingleton<ITerminalService, TerminalService>();
        services.AddSingleton<WelcomeViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        var modules = new IModule[]
        {
            new Fluence.Modules.FileExplorer.Entrypoint(),
            new Fluence.Modules.SolutionView.Entrypoint(),
            new Fluence.Modules.Editor.Entrypoint(),
            new Fluence.Modules.NuGetExplorer.Entrypoint(),
            new Fluence.Modules.Terminal.Entrypoint(),
            new Fluence.Modules.DotnetCli.Entrypoint(),
            new Fluence.Modules.DebuggerSetup.Entrypoint(),
            new Fluence.Modules.Debug.Entrypoint(),
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
