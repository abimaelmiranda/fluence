using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Modules;

namespace Fluence.Modules.LanguageServer;

public sealed partial class Entrypoint
{
    private static Task HandleNavigation(IModuleHost host, INavigationService nav, string kind, string filePath, int line, int character, CancellationToken ct) =>
        ResolveAndPublishNavigation(host, nav, kind, filePath, line, character, ct);

    private static async Task ResolveAndPublishNavigation(
        IModuleHost host,
        INavigationService nav,
        string kind,
        string filePath,
        int line,
        int character,
        CancellationToken ct)
    {
        try
        {
            var location = kind switch
            {
                "definition" => await nav.GetDefinitionAsync(filePath, line, character, ct),
                "implementation" => await nav.GetImplementationAsync(filePath, line, character, ct),
                "typeDefinition" => await nav.GetTypeDefinitionAsync(filePath, line, character, ct),
                _ => null,
            };

            if (location is null)
                return;

            if (!string.Equals(location.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                host.Events.Publish(new OpenFileRequestedEvent(location.FilePath));

            host.Events.Publish(new NavigationResolvedEvent(location.FilePath, location.Line, location.Character));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[LS] navigation failed: {ex.Message}"); }
    }

}
