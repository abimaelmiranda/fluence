using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Abstractions.LanguageServer;

public interface ILspProvisioningService
{
    bool IsProvisioned();

    string GetExecutablePath();

    /// <summary>
    /// Returns environment variables required to run the language server process.
    /// May return an empty dictionary for language servers that need no special environment.
    /// </summary>
    IReadOnlyDictionary<string, string> GetLaunchEnvironment();

    Task ProvisionAsync(Action<string> onOutput, CancellationToken cancellationToken = default);
}
