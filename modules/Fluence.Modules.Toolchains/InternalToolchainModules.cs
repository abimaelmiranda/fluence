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

    public async Task InitializeAsync(IModuleHost host, CancellationToken cancellationToken)
    {
        await _languageServer.InitializeAsync(host, cancellationToken).ConfigureAwait(false);
        await _debug.InitializeAsync(host, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await _debug.DisposeAsync().ConfigureAwait(false);
        await _languageServer.DisposeAsync().ConfigureAwait(false);
    }

    public async Task StopAsync(ModuleShutdownContext context)
    {
        if (_debug is IModuleShutdownParticipant debug)
            await debug.StopAsync(context).ConfigureAwait(false);
        if (_languageServer is IModuleShutdownParticipant languageServer)
            await languageServer.StopAsync(context).ConfigureAwait(false);
    }
}
