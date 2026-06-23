using System.Resources;
using System.Threading.Tasks;
using System.Threading;
using Fluence.Core.Abstractions.Keybindings;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Output;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Abstractions.Theming;
using Fluence.Core.Json;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Keybindings;
using Fluence.Core.Services.Settings;
using Fluence.Modules.Debug.Json;
using Fluence.Modules.Editor.Json;
using Fluence.Modules.LanguageServer.Json;
using Fluence.Modules.Settings.Services;
using Fluence.Modules.Settings.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.Settings;

public sealed class Entrypoint : IModule
{
    public string Id => "Settings";

    public string DisplayName => "Settings";

    public const string ChannelId = "settings";

    public int StartupOrder => 100;

    public ModuleContributions GetContributions() =>
        new() { OutputChannel = new OutputChannelDescriptor(ChannelId, "Settings") };

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<ISettingsRegistry>(_ =>
        {
            var registry = new SettingsRegistry();
            registry.Register(FluenceCoreSettingsJsonContext.Default.GlobalSettings);
            registry.Register(EditorSettingsJsonContext.Default.EditorSettings);
            registry.Register(LanguageServerSettingsJsonContext.Default.LanguageServerSettings);
            registry.Register(DebugSettingsJsonContext.Default.DebugSettings);
            registry.Register(FluenceCoreSettingsJsonContext.Default.ShellSettings);
            registry.Register(FluenceCoreSettingsJsonContext.Default.ProblemsSettings);
            return registry;
        });

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IKeybindingService, KeybindingService>();
        services.AddSingleton<IThemeLoader, ThemeLoader>();
        services.AddSingleton<ThemeRuntimeCoordinator>();
        services.AddSingleton<ICommandRegistry, CommandRegistry>();

        services.AddSingleton<LanguageRuntimeCoordinator>();
        services.AddSingleton<ISettingsTool, SettingsToolViewModel>();
        services.AddSingleton<SettingsToolViewModel>(
            sp => (SettingsToolViewModel)sp.GetRequiredService<ISettingsTool>());
    }

    public Task InitializeAsync(IModuleHost host, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        host.Services.GetRequiredService<ThemeRuntimeCoordinator>().Start();
        host.Services.GetRequiredService<LanguageRuntimeCoordinator>().Start();

        host.Services.GetRequiredService<ILocalizationService>()
            .Register(new ResourceManager(
                "Fluence.Modules.Settings.Resources.Strings",
                typeof(Entrypoint).Assembly));

        host.SetModuleState(Id, ModuleState.Active);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
