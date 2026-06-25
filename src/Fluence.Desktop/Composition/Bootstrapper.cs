using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Services.Debugging;
using Fluence.Core.Abstractions.Infrastructure;
using Fluence.Core.Abstractions.Lifecycle;
using Fluence.Core.Models.Infrastructure;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Services.Modules;
using Fluence.Core.Abstractions.Dialogs;
using Fluence.Core.Abstractions.File;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Models.Output;
using Fluence.Core.Abstractions.Problems;
using Fluence.Core.Abstractions.Storage;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Services.File;
using Fluence.Core.Services.Jobs;
using Fluence.Core.Services.Problems;
using Fluence.Core.Abstractions.Jobs;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Services.Localization;
using Fluence.Desktop.Markup;
using Fluence.Desktop.Services;
using Fluence.Desktop.ViewModels;
using Fluence.Desktop.Views;
using Fluence.Infrastructure;
using Fluence.Infrastructure.Tasks;
using Fluence.Infrastructure.Pty;
using Fluence.Infrastructure.Protocols.Dap;
using Fluence.Infrastructure.Protocols.Lsp;
using Fluence.Core.Abstractions.Languages;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Services.Languages;
using Fluence.Infrastructure.Languages;
using SettingsEntrypoint = Fluence.Modules.Settings.Entrypoint;
using NuGetExplorerEntrypoint = Fluence.Modules.NuGetExplorer.Entrypoint;
using TerminalEntrypoint = Fluence.Modules.Terminal.Entrypoint;
using ToolchainsEntrypoint = Fluence.Modules.Toolchains.Entrypoint;
using SourceControlEntrypoint = Fluence.Modules.SourceControl.Entrypoint;
using WorkbenchEntrypoint = Fluence.Modules.Workbench.Entrypoint;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Fluence.Core.Abstractions.Projects;
using System.IO;

namespace Fluence.Desktop.Composition;

internal static class Bootstrapper
{
    public static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug));

        services.AddSingleton<IFileViewerRegistry, FileViewerRegistry>();

        services.AddSingleton<ILocalizationService, LocalizationService>();

        var viewRegistry = new ViewRegistry();
        viewRegistry.Register<WelcomeViewModel, WelcomeView>();
        services.AddSingleton<IViewRegistry>(viewRegistry);
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<ITaskScheduler>(provider =>
            new FluentTaskScheduler(provider.GetRequiredService<IOutputChannelService>()));
        services.AddSingleton<IShellEventBus>(provider => new ShellEventBus(
            provider.GetRequiredService<IUiDispatcher>(),
            provider.GetRequiredService<IOutputChannelService>()));
        services.AddSingleton<IShellRequestBus, ShellRequestBus>();
        services.AddSingleton<ShellRegionHost>();
        services.AddSingleton<IShellRegionHost>(provider => provider.GetRequiredService<ShellRegionHost>());
        services.AddSingleton<IWorkspaceContext>(provider => new WorkspaceContext(provider.GetRequiredService<IUiDispatcher>()));
        services.AddSingleton<ILanguageProfileRegistry>(provider =>
        {
            var registry = new LanguageProfileRegistry();
            registry.Register(new CSharpLanguageProfile());
            registry.Register(new CppLanguageProfile());
            registry.Register(new CLanguageProfile());
            return registry;
        });
        services.AddSingleton<IModuleHost, ModuleHost>();
        services.AddSingleton<IShutdownCoordinator, ShutdownCoordinator>();
        services.AddSingleton<AvaloniaApplicationLifecycleService>();
        services.AddSingleton<IApplicationLifecycleService>(provider =>
            provider.GetRequiredService<AvaloniaApplicationLifecycleService>());
        services.AddSingleton<IWorkspaceDialogService, AvaloniaWorkspaceDialogService>();
        services.AddSingleton<ILaunchSetupDialogService, AvaloniaLaunchSetupDialogService>();
        services.AddSingleton<AvaloniaUserNotificationService>();
        services.AddSingleton<IUserNotificationService>(provider => provider.GetRequiredService<AvaloniaUserNotificationService>());
        services.AddSingleton<IOutputChannelService, OutputChannelService>();
        services.AddSingleton<IOutputChannelRegistry>(_ =>
        {
            var registry = new OutputChannelRegistry();
            registry.Register(new(OutputChannelIds.Output, "Output"));
            registry.Register(new(OutputChannelIds.Debug, "Debug"));
            registry.Register(new(OutputChannelIds.Run, "Run"));
            registry.Register(new(OutputChannelIds.Memory, "Memory"));
            return registry;
        });
        services.AddSingleton<IProblemService, ProblemService>();
        services.AddSingleton<IExclusiveJobCoordinator, ExclusiveJobCoordinator>();
        services.AddSingleton<IFileClipboardService, FileClipboardService>();
        services.AddSingleton<IFileOperationDialogService, AvaloniaFileOperationDialogService>();
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<IFluenceStorageService, FluenceStorageService>();
        services.AddSingleton<IProcessSpawner, ProcessSpawner>();
        services.AddSingleton<IProcessHost, ProcessHost>();
        services.AddSingleton<IDotnetSdkProvisioningService, DotnetSdkProvisioningService>();
        services.AddSingleton<ILaunchSettingsService, LaunchSettingsService>();
        services.AddSingleton<ILaunchSettingsCoordinator, LaunchSettingsCoordinator>();
        services.AddSingleton<Func<string, string, CancellationToken, Task<IDebugAdapterClient>>>(sp =>
        {
            var storage = sp.GetRequiredService<IFluenceStorageService>();
            var spawner = sp.GetRequiredService<IProcessSpawner>();
            return (workspaceRoot, adapterExecutable, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                if (!File.Exists(adapterExecutable))
                    throw new FileNotFoundException("Debug adapter executable was not found.", adapterExecutable);
                var logPath = storage.GetProjectPath(workspaceRoot, $"logs/dap-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");
                var adapterId = Path.GetFileNameWithoutExtension(adapterExecutable);
                return Task.FromResult<IDebugAdapterClient>(new DapClient(adapterExecutable, adapterId, logPath, spawner));
            };
        });
        services.AddSingleton<OmniSharpProvisioningService>();
        services.AddSingleton<ClangdProvisioningService>();
        services.AddSingleton<DebuggerProvisioningService>();
        services.AddSingleton<ILspProvisioningService>(sp => sp.GetRequiredService<OmniSharpProvisioningService>());
        services.AddSingleton<IDebuggerProvisioningService>(sp => sp.GetRequiredService<DebuggerProvisioningService>());
        services.AddSingleton<IPtyHost>(_ =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? new PortaMacOsPtyHost()
                : new WindowsPtyHost());
        services.AddSingleton<ITerminalService, TerminalService>();
        services.AddSingleton<IRecentProjectsService, RecentProjectsService>();
        services.AddSingleton<IWorkspaceSnapshotService, WorkspaceSnapshotService>();
        services.AddSingleton<WorkspaceSnapshotCoordinator>();
        services.AddSingleton<MemoryMonitorService>();
        services.AddSingleton<ApplicationStartupCoordinator>();
        services.AddSingleton<IStartupCoordinator>(provider =>
            provider.GetRequiredService<ApplicationStartupCoordinator>());
        services.AddSingleton<WelcomeViewModel>();
        services.AddSingleton<ActivityBarViewModel>();
        services.AddSingleton<ProblemsViewModel>();
        services.AddSingleton<BottomBarViewModel>();
        services.AddSingleton<FileIndexService>();
        services.AddSingleton<QuickOpenViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        var modules = new IModule[]
        {
            new SettingsEntrypoint(),
            new WorkbenchEntrypoint(),
            new NuGetExplorerEntrypoint(),
            new TerminalEntrypoint(),
            new ToolchainsEntrypoint(),
            new SourceControlEntrypoint(),
        };

        foreach (var module in modules)
        {
            module.Register(services);
        }

        services.AddSingleton<IReadOnlyList<IModule>>(modules);

        return services.BuildServiceProvider();
    }

    public static Task ShutdownModulesAsync(
        IServiceProvider serviceProvider,
        IProgress<ModuleShutdownProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        serviceProvider.GetRequiredService<IShutdownCoordinator>()
            .ShutdownAsync(progress, cancellationToken);
}
