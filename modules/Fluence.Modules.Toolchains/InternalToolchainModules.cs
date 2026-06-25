using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Output;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Output;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.Toolchains;

internal sealed class InternalToolchainModules
{
    private readonly IModule _dotnetCli = new Fluence.Modules.DotnetCli.Entrypoint();
    private readonly IModule _languageServer = new Fluence.Modules.LanguageServer.Entrypoint();
    private readonly IModule _debug = new Fluence.Modules.Debug.Entrypoint();

    public void Register(IServiceCollection services)
    {
        _dotnetCli.Register(services);
        _languageServer.Register(services);
        _debug.Register(services);
    }

    public IReadOnlyList<ShellPanelContribution> DebugPanels => _debug.GetContributions().Panels;

    public void RegisterOutputChannels(IOutputChannelRegistry registry)
    {
        registry.Register(new OutputChannelDescriptor(Fluence.Modules.LanguageServer.Entrypoint.ChannelId, "Language Server"));
        registry.Register(new OutputChannelDescriptor(Fluence.Modules.Debug.Entrypoint.ChannelId, "Debug"));
    }

    public Task InitializeAsync(IModuleHost host, CancellationToken cancellationToken) =>
        Task.WhenAll(
            _languageServer.InitializeAsync(host, cancellationToken),
            _debug.InitializeAsync(host, cancellationToken));

    public ValueTask DisposeAsync() =>
        new(Task.WhenAll(
            _debug.DisposeAsync().AsTask(),
            _languageServer.DisposeAsync().AsTask()));

    public Task StopAsync(ModuleShutdownContext context)
    {
        var tasks = new List<Task>(2);
        if (_debug is IModuleShutdownParticipant debug)
            tasks.Add(debug.StopAsync(context));
        if (_languageServer is IModuleShutdownParticipant languageServer)
            tasks.Add(languageServer.StopAsync(context));
        return tasks.Count > 0 ? Task.WhenAll(tasks) : Task.CompletedTask;
    }
}
