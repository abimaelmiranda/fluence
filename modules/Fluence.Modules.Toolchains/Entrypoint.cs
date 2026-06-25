using System;
using System.Collections.Generic;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Languages;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Abstractions.Projects;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Abstractions.Toolchains;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Events.Build;
using Fluence.Core.Events.Debug;
using Fluence.Core.Events.Provisioning;
using Fluence.Core.Events.Toolchains;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Models.Output;
using Fluence.Core.Models.Toolchains;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Services.Toolchains;
using Fluence.Modules.Debug;
using Fluence.Modules.DotnetCli.ViewModels;
using Fluence.Modules.DotnetCli.Services;
using Fluence.Modules.Toolchains.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using DotnetCliEntrypoint = Fluence.Modules.DotnetCli.Entrypoint;
using Fluence.Core.Events.Workspace;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Modules.Toolchains;

public sealed class Entrypoint : IModule, IModuleShutdownParticipant, IConditionalModule
{
    private readonly InternalToolchainModules _modules = new();
    private readonly List<IDisposable> _subscriptions = [];

    public string Id => "Toolchains";

    public string DisplayName => "Toolchains";

    public int StartupOrder => 700;

    public bool ShouldActivate(IWorkspaceContext workspace, ILanguageProfileRegistry profiles)
    {
        var languageId = profiles.DetectWorkspaceLanguage(workspace);
        return languageId is null or "csharp" or "c" or "cpp";
    }

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IToolchainRegistry, ToolchainRegistry>();
        services.AddSingleton<CSharpToolchain>();
        services.AddSingleton<ToolchainSetupViewModel>();

        _modules.Register(services);

        services.AddKeyedSingleton<IRunService>("csharp",
            (sp, _) => sp.GetRequiredService<DotnetRunService>());
        services.AddKeyedSingleton<IDebugService>("csharp",
            (sp, _) => sp.GetRequiredService<DebugService>());
    }

    public ModuleContributions GetContributions() =>
        new()
        {
            Panels = _modules.DebugPanels,
            OutputChannel = new OutputChannelDescriptor(DotnetCliEntrypoint.ChannelId, ".NET CLI"),
        };

    public async Task InitializeAsync(IModuleHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        host.Services.GetRequiredService<ILocalizationService>()
            .Register(new ResourceManager("Fluence.Modules.DotnetCli.Resources.Strings", typeof(DotnetCliEntrypoint).Assembly));

        var outputRegistry = host.Services.GetRequiredService<IOutputChannelRegistry>();
        _modules.RegisterOutputChannels(outputRegistry);

        var registry = host.Services.GetRequiredService<IToolchainRegistry>();
        registry.Register(host.Services.GetRequiredService<CSharpToolchain>());
        registry.Register(new RoutedToolchain("c", host.Services, host.Events));
        registry.Register(new RoutedToolchain("cpp", host.Services, host.Events));

        await _modules.InitializeAsync(host, cancellationToken).ConfigureAwait(false);

        var scheduler = host.Services.GetRequiredService<ITaskScheduler>();

        _subscriptions.Add(host.Events.SubscribeSync<BuildWorkspaceRequestedEvent>(_ =>
            Schedule(registry, scheduler, "toolchain.build", TaskPriority.Maintenance, ToolchainCommandKind.BuildWorkspace)));
        _subscriptions.Add(host.Events.SubscribeSync<TestWorkspaceRequestedEvent>(_ =>
            Schedule(registry, scheduler, "toolchain.test", TaskPriority.Maintenance, ToolchainCommandKind.TestWorkspace)));
        _subscriptions.Add(host.Events.SubscribeSync<RestoreWorkspaceRequestedEvent>(_ =>
            Schedule(registry, scheduler, "toolchain.restore", TaskPriority.Maintenance, ToolchainCommandKind.RestoreWorkspace)));
        _subscriptions.Add(host.Events.SubscribeSync<CleanWorkspaceRequestedEvent>(_ =>
            Schedule(registry, scheduler, "toolchain.clean", TaskPriority.Maintenance, ToolchainCommandKind.CleanWorkspace)));
        _subscriptions.Add(host.Events.SubscribeSync<BuildProjectRequestedEvent>(e =>
            Schedule(registry, scheduler, "toolchain.build", TaskPriority.Maintenance, ToolchainCommandKind.BuildProject, e.ProjectPath)));
        _subscriptions.Add(host.Events.SubscribeSync<TestProjectRequestedEvent>(e =>
            Schedule(registry, scheduler, "toolchain.test", TaskPriority.Maintenance, ToolchainCommandKind.TestProject, e.ProjectPath)));
        _subscriptions.Add(host.Events.SubscribeSync<RestoreProjectRequestedEvent>(e =>
            Schedule(registry, scheduler, "toolchain.restore", TaskPriority.Maintenance, ToolchainCommandKind.RestoreProject, e.ProjectPath)));
        _subscriptions.Add(host.Events.SubscribeSync<CleanProjectRequestedEvent>(e =>
            Schedule(registry, scheduler, "toolchain.clean", TaskPriority.Maintenance, ToolchainCommandKind.CleanProject, e.ProjectPath)));
        _subscriptions.Add(host.Events.SubscribeSync<RunProjectRequestedEvent>(_ =>
            Schedule(registry, scheduler, "toolchain.run", TaskPriority.Interactive, ToolchainCommandKind.RunProject)));
        _subscriptions.Add(host.Events.SubscribeSync<RunSpecificProjectRequestedEvent>(e =>
            Schedule(registry, scheduler, "toolchain.run", TaskPriority.Interactive, ToolchainCommandKind.RunSpecificProject, e.ProjectPath)));
        _subscriptions.Add(host.Events.SubscribeSync<DebugProjectRequestedEvent>(_ =>
            Schedule(registry, scheduler, "toolchain.debug", TaskPriority.Interactive, ToolchainCommandKind.DebugProject)));

        _subscriptions.Add(host.Events.SubscribeSync<NewProjectRequestedEvent>(_ => OpenNewProjectWizard(host)));
        _subscriptions.Add(host.Events.SubscribeSync<PublishProjectRequestedEvent>(e => OpenPublishWizard(host, e.ProjectPath)));
        _subscriptions.Add(host.Events.SubscribeSync<DotnetSdkSetupRequestedEvent>(_ => OpenSdkSetup(host)));
        _subscriptions.Add(host.Events.SubscribeSync<ToolchainProvisioningRequiredEvent>(e => OpenProvisioning(host, scheduler, e)));
        _subscriptions.Add(host.Events.SubscribeSync<LspProvisioningRequiredEvent>(_ =>
            OpenProvisioning(host, scheduler, new ToolchainProvisioningRequiredEvent("csharp", ToolchainCapability.LanguageServer))));
        _subscriptions.Add(host.Events.SubscribeSync<DebuggerProvisioningRequiredEvent>(_ =>
            OpenProvisioning(host, scheduler, new ToolchainProvisioningRequiredEvent("csharp", ToolchainCapability.Debugger))));

        host.SetModuleState(Id, ModuleState.Active);
    }

    private static void Schedule(
        IToolchainRegistry registry,
        ITaskScheduler scheduler,
        string ownerId,
        TaskPriority priority,
        ToolchainCommandKind kind,
        string? projectPath = null) =>
        scheduler.Schedule(ownerId, priority, ct => registry.ExecuteAsync(new ToolchainCommand(kind, projectPath), ct));

    private static void OpenNewProjectWizard(IModuleHost host)
    {
        var vm = host.Services.GetRequiredService<NewProjectWizardViewModel>();
        host.Workspace.OpenToolTab(ToolTabIds.NewProject, ToolTabIds.NewProjectTitle, vm);
    }

    private static void OpenPublishWizard(IModuleHost host, string? projectPath)
    {
        var vm = host.Services.GetRequiredService<PublishWizardViewModel>();
        vm.Load(projectPath);
        host.Workspace.OpenToolTab(ToolTabIds.Publish, ToolTabIds.PublishTitle, vm);
    }

    private static void OpenSdkSetup(IModuleHost host)
    {
        var vm = host.Services.GetRequiredService<DotnetSdkSetupViewModel>();
        host.Workspace.OpenToolTab(ToolTabIds.DotnetSdkSetup, ToolTabIds.DotnetSdkSetupTitle, vm);
        _ = vm.RefreshAsync();
    }

    private static void OpenProvisioning(
        IModuleHost host,
        ITaskScheduler scheduler,
        ToolchainProvisioningRequiredEvent e)
    {
        if (e.Capability == ToolchainCapability.Sdk)
        {
            OpenSdkSetup(host);
            return;
        }

        var vm = host.Services.GetRequiredService<ToolchainSetupViewModel>();
        vm.Load(e.ToolchainId, e.Capability);
        host.Workspace.OpenToolTab(vm.ToolTabId, vm.Title, vm);
        scheduler.Schedule(
            $"toolchain.provision.{e.ToolchainId}.{e.Capability}",
            TaskPriority.Background,
            ct => vm.StartProvisioningAsync(ct));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var subscription in _subscriptions)
            subscription.Dispose();
        _subscriptions.Clear();

        await _modules.DisposeAsync().ConfigureAwait(false);
    }

    public async Task StopAsync(ModuleShutdownContext context)
    {
        await _modules.StopAsync(context).ConfigureAwait(false);
    }
}
