using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Abstractions.LanguageServer;

public interface ILspProvisioningService
{
    bool IsProvisioned();

    string GetExecutablePath();

    string? GetDotnetHostPath();

    string? GetSelectedSdkPath(string? rootPath = null);

    /// <summary>
    /// Returns environment variables required to run the language server (e.g. DOTNET_ROOT, DOTNET_HOST_PATH).
    /// </summary>
    IReadOnlyDictionary<string, string> GetLaunchEnvironment();

    Task ProvisionAsync(Action<string> onOutput, CancellationToken cancellationToken = default);
}
