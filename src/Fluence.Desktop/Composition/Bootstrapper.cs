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
using Fluence.Core.Abstractions.Projects;
using Fluence.Core.Services.Languages;
using Fluence.Infrastructure.Languages;
using Fluence.Infrastructure.Languages.Routers;
using Fluence.Infrastructure.Projects;
using Fluence.Modules.DotnetCli.Services;
using SettingsEntrypoint = Fluence.Modules.Settings.Entrypoint;
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
using XamlViewerEntrypoint = Fluence.Modules.XamlViewer.Entrypoint;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Fluence.Modules.Debug;

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
        services.AddSingleton<IProjectTemplateProvider, CppProjectTemplateProvider>();
        services.AddSingleton<CppRunService>();
        services.AddSingleton<CppDebugService>();
        services.AddSingleton<IDotnetSdkProvisioningService, DotnetSdkProvisioningService>();
        services.AddSingleton<ILaunchSettingsService, LaunchSettingsService>();
        services.AddSingleton<ILaunchSettingsCoordinator, LaunchSettingsCoordinator>();
        services.AddSingleton<IDebugAdapterClientFactory, DapDebugAdapterClientFactory>();
        // Concretos sem alias de interface não-keyed
        services.AddSingleton<OmniSharpProvisioningService>();
        services.AddSingleton<ClangdProvisioningService>();
        services.AddSingleton<DebuggerProvisioningService>();
        services.AddSingleton<CppDebuggerProvisioningService>();

        // IDotnetLspProvisioningService mantido — usado por OmniSharpArgumentsBuilder via cast
        services.AddSingleton<IDotnetLspProvisioningService>(
            sp => sp.GetRequiredService<OmniSharpProvisioningService>());

        // Keyed por linguagem — C# usa OmniSharp, C/C++ usa clangd
        services.AddKeyedSingleton<ILspProvisioningService>("csharp",
            (sp, _) => (ILspProvisioningService)sp.GetRequiredService<OmniSharpProvisioningService>());
        services.AddKeyedSingleton<ILspProvisioningService>("c",
            (sp, _) => (ILspProvisioningService)sp.GetRequiredService<ClangdProvisioningService>());
        services.AddKeyedSingleton<ILspProvisioningService>("cpp",
            (sp, _) => (ILspProvisioningService)sp.GetRequiredService<ClangdProvisioningService>());
        services.AddKeyedSingleton<IRunService>("csharp",
            (sp, _) => sp.GetRequiredService<DotnetRunService>());
        services.AddKeyedSingleton<IRunService>("c",
            (sp, _) => sp.GetRequiredService<CppRunService>());
        services.AddKeyedSingleton<IRunService>("cpp",
            (sp, _) => sp.GetRequiredService<CppRunService>());
        services.AddKeyedSingleton<IDebuggerProvisioningService>("csharp",
            (sp, _) => (IDebuggerProvisioningService)sp.GetRequiredService<DebuggerProvisioningService>());
        services.AddKeyedSingleton<IDebuggerProvisioningService>("c",
            (sp, _) => sp.GetRequiredService<CppDebuggerProvisioningService>());
        services.AddKeyedSingleton<IDebuggerProvisioningService>("cpp",
            (sp, _) => sp.GetRequiredService<CppDebuggerProvisioningService>());
        services.AddKeyedSingleton<IDebugService>("csharp",
            (sp, _) => sp.GetRequiredService<DebugService>());
        services.AddKeyedSingleton<IDebugService>("c",
            (sp, _) => sp.GetRequiredService<CppDebugService>());
        services.AddKeyedSingleton<IDebugService>("cpp",
            (sp, _) => sp.GetRequiredService<CppDebugService>());

        // Routers como singletons não-keyed — consumers injetam as mesmas interfaces de sempre
        services.AddSingleton<ILspProvisioningService, LspProvisioningRouter>();
        services.AddSingleton<ILspArgumentsBuilder, LspArgumentsBuilderRouter>();
        services.AddSingleton<IDebuggerProvisioningService, DebuggerProvisioningRouter>();
        services.AddSingleton<IDebugService, DebugServiceRouter>();
        services.AddSingleton<IRunService, RunServiceRouter>();
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
            new XamlViewerEntrypoint(),
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
