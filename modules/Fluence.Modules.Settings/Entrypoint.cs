using Fluence.Core.Abstractions.Keybindings;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Abstractions.Theming;
using Fluence.Core.Json;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Keybindings;
using Fluence.Core.Services.Settings;
using Fluence.Modules.Settings.Services;
using Fluence.Modules.Settings.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.Settings;

public sealed class Entrypoint : IModule
{
    public string Name => "Settings";

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<ISettingsRegistry>(_ =>
        {
            var registry = new SettingsRegistry();
            registry.Register(FluenceCoreSettingsJsonContext.Default.GlobalSettings);
            registry.Register(FluenceCoreSettingsJsonContext.Default.ShellSettings);
            registry.Register(FluenceCoreSettingsJsonContext.Default.ProblemsSettings);
            return registry;
        });

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IKeybindingService, KeybindingService>();
        services.AddSingleton<IThemeLoader, ThemeLoader>();
        services.AddSingleton<ThemeRuntimeCoordinator>();
        services.AddSingleton<ICommandRegistry, CommandRegistry>();

        services.AddSingleton<ISettingsTool, SettingsToolViewModel>();
        services.AddSingleton<SettingsToolViewModel>(
            sp => (SettingsToolViewModel)sp.GetRequiredService<ISettingsTool>());
    }

    public void Initialize(IModuleHost host)
    {
        host.Services.GetRequiredService<ThemeRuntimeCoordinator>().Start();
        host.SetModuleState(Name, ModuleState.Active);
    }
}
