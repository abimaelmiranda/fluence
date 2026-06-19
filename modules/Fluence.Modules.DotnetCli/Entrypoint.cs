using System;
using System.Collections.Generic;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Dotnet;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Jobs;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Notifications;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Models.Jobs;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Models.Workbench;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.DotnetCli.Commands;
using Fluence.Modules.DotnetCli.Models;
using Fluence.Modules.DotnetCli.Services;
using Fluence.Modules.DotnetCli.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Fluence.Modules.DotnetCli.Commands.Project.Run;
using Fluence.Modules.DotnetCli.Commands.Project.Clean;
using Fluence.Modules.DotnetCli.Commands.Project.Test;
using Fluence.Modules.DotnetCli.Commands.Project.Build;
using Fluence.Modules.DotnetCli.Commands.Project.Restore;
using Fluence.Modules.DotnetCli.Commands.Workspace.Restore;
using Fluence.Modules.DotnetCli.Commands.Workspace.Clean;
using Fluence.Modules.DotnetCli.Commands.Workspace.Build;
using Fluence.Modules.DotnetCli.Commands.Workspace.Test;
using Fluence.Core.Events.Build;
using Fluence.Core.Events.Ui;
using Fluence.Core.Events.Workspace;

namespace Fluence.Modules.DotnetCli;

public sealed class Entrypoint : IModule
{
    private readonly List<IDisposable> _subscriptions = [];

    public string Id => "DotnetCli";

    public string DisplayName => ".NET CLI";

    public int StartupOrder => 700;

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IProjectExecutionTargetResolver, DotnetProjectExecutionTargetResolver>();
        services.AddSingleton<RunTargetResolver>();
        services.AddSingleton<NewProjectWizardViewModel>();
        services.AddSingleton<PublishWizardViewModel>();
        services.AddSingleton<DotnetSdkSetupViewModel>();
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
    }

    public Task InitializeAsync(IModuleHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scheduler = host.Services.GetRequiredService<ITaskScheduler>();
        host.Services.GetRequiredService<ILocalizationService>()
            .Register(new ResourceManager("Fluence.Modules.DotnetCli.Resources.Strings", typeof(Entrypoint).Assembly));

        // Build/Test/Restore/Clean são pesadas — Maintenance para não competir com o editor
        _subscriptions.Add(host.Events.SubscribeSync<BuildWorkspaceRequestedEvent>(_ =>
            scheduler.Schedule("dotnet.build", TaskPriority.Maintenance,
                ct => HandleAsync(host, new BuildWorkspaceCommand(), ct))));
        _subscriptions.Add(host.Events.SubscribeSync<TestWorkspaceRequestedEvent>(_ =>
            scheduler.Schedule("dotnet.test", TaskPriority.Maintenance,
                ct => HandleAsync(host, new TestWorkspaceCommand(), ct))));
        _subscriptions.Add(host.Events.SubscribeSync<RestoreWorkspaceRequestedEvent>(_ =>
            scheduler.Schedule("dotnet.restore", TaskPriority.Maintenance,
                ct => HandleAsync(host, new RestoreWorkspaceCommand(), ct))));
        _subscriptions.Add(host.Events.SubscribeSync<CleanWorkspaceRequestedEvent>(_ =>
            scheduler.Schedule("dotnet.clean", TaskPriority.Maintenance,
                ct => HandleAsync(host, new CleanWorkspaceCommand(), ct))));
        _subscriptions.Add(host.Events.SubscribeSync<BuildProjectRequestedEvent>(e =>
            scheduler.Schedule("dotnet.build", TaskPriority.Maintenance,
                ct => HandleAsync(host, new BuildProjectCommand(e.ProjectPath), ct))));
        _subscriptions.Add(host.Events.SubscribeSync<TestProjectRequestedEvent>(e =>
            scheduler.Schedule("dotnet.test", TaskPriority.Maintenance,
                ct => HandleAsync(host, new TestProjectCommand(e.ProjectPath), ct))));
        _subscriptions.Add(host.Events.SubscribeSync<RestoreProjectRequestedEvent>(e =>
            scheduler.Schedule("dotnet.restore", TaskPriority.Maintenance,
                ct => HandleAsync(host, new RestoreProjectCommand(e.ProjectPath), ct))));
        _subscriptions.Add(host.Events.SubscribeSync<CleanProjectRequestedEvent>(e =>
            scheduler.Schedule("dotnet.clean", TaskPriority.Maintenance,
                ct => HandleAsync(host, new CleanProjectCommand(e.ProjectPath), ct))));

        // Run é Interactive — usuário quer feedback imediato
        _subscriptions.Add(host.Events.SubscribeSync<RunProjectRequestedEvent>(_ =>
            scheduler.Schedule("dotnet.run", TaskPriority.Interactive,
                ct => RunAsync(host, ct))));
        _subscriptions.Add(host.Events.SubscribeSync<RunSpecificProjectRequestedEvent>(e =>
            scheduler.Schedule("dotnet.run", TaskPriority.Interactive,
                ct => RunSpecificAsync(host, e.ProjectPath, ct))));

        // UI-only: abrem tool tabs, sem trabalho pesado
        _subscriptions.Add(host.Events.SubscribeSync<NewProjectRequestedEvent>(_ => OpenNewProjectWizard(host)));
        _subscriptions.Add(host.Events.SubscribeSync<PublishProjectRequestedEvent>(e => OpenPublishWizard(host, e.ProjectPath)));
        _subscriptions.Add(host.Events.SubscribeSync<DotnetSdkSetupRequestedEvent>(_ => OpenSdkSetup(host)));

        host.SetModuleState(Id, ModuleState.Active);
        return Task.CompletedTask;
    }

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

    private static async Task HandleAsync<TCommand>(IModuleHost host, TCommand command, CancellationToken ct)
    {
        if (!await EnsureSdkAsync(host).ConfigureAwait(false))
            return;

        host.Events.Publish(new SelectBottomBarTabEvent(BottomBarTabIds.Run));
        var handler = host.Services.GetRequiredService<ICommandHandler<TCommand>>();
        await handler.HandleAsync(command, ct);
    }

    private static async Task RunAsync(IModuleHost host, CancellationToken ct)
    {
        if (!await EnsureSdkAsync(host).ConfigureAwait(false))
            return;

        await RunExclusiveAsync(host, async () =>
        {
            var handler = host.Services.GetRequiredService<ICommandHandler<RunProjectCommand>>();
            await handler.HandleAsync(new RunProjectCommand(), ct);
        });
    }

    private static async Task RunSpecificAsync(IModuleHost host, string projectPath, CancellationToken ct)
    {
        if (!await EnsureSdkAsync(host).ConfigureAwait(false))
            return;

        await RunExclusiveAsync(host, async () =>
        {
            host.Events.Publish(new SelectBottomBarTabEvent(BottomBarTabIds.Run));
            var handler = host.Services.GetRequiredService<ICommandHandler<RunSpecificProjectCommand>>();
            await handler.HandleAsync(new RunSpecificProjectCommand(projectPath), ct);
        });
    }

    private static async Task RunExclusiveAsync(IModuleHost host, Func<Task> run)
    {
        var jobs = host.Services.GetRequiredService<IExclusiveJobCoordinator>();
        if (!jobs.TryAcquire(ExclusiveJobKind.Run, out var lease))
        {
            var activeName = jobs.ActiveJob switch
            {
                ExclusiveJobKind.Debug => "debug session",
                ExclusiveJobKind.Run => "run process",
                _ => "job",
            };
            host.Services.GetRequiredService<IUserNotificationService>()
                .ShowWarning("Run", $"Cannot start run while a {activeName} is running.");
            return;
        }

        using (lease!)
        {
            await run().ConfigureAwait(false);
        }
    }

    private static async Task<bool> EnsureSdkAsync(IModuleHost host)
    {
        var sdk = host.Services.GetRequiredService<IDotnetSdkProvisioningService>();
        var status = await sdk.GetStatusAsync().ConfigureAwait(false);
        if (status.IsDotnetAvailable && status.InstalledSdks.Count > 0)
            return true;

        host.Events.Publish(new DotnetSdkSetupRequestedEvent());
        return false;
    }

    public ValueTask DisposeAsync()
    {
        foreach (var subscription in _subscriptions)
            subscription.Dispose();
        _subscriptions.Clear();
        return ValueTask.CompletedTask;
    }
}
