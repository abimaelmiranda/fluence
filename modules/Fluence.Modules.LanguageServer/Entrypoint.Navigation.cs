using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Fluence.Modules.LanguageServer;

public sealed partial class Entrypoint
{
    private static Task HandleNavigation(IModuleHost host, ILanguageServerService lsp, string kind, string filePath, int line, int character)
    {
        var nav = host.Services.GetRequiredService<INavigationService>();
        return ResolveAndPublishNavigation(host, nav, kind, filePath, line, character);
    }

    private static async Task ResolveAndPublishNavigation(
        IModuleHost host,
        INavigationService nav,
        string kind,
        string filePath,
        int line,
        int character)
    {
        try
        {
            var location = kind switch
            {
                "definition" => await nav.GetDefinitionAsync(filePath, line, character),
                "implementation" => await nav.GetImplementationAsync(filePath, line, character),
                "typeDefinition" => await nav.GetTypeDefinitionAsync(filePath, line, character),
                _ => null,
            };

            if (location is null)
                return;

            // Open the file first, then navigate to the position
            if (!string.Equals(location.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                host.Events.Publish(new OpenFileRequestedEvent(location.FilePath));

            host.Events.Publish(new NavigationResolvedEvent(location.FilePath, location.Line, location.Character));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine($"[LS] navigation failed: {ex.Message}"); }
    }

    private static void SafeSend(Task task) =>
        task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
}
