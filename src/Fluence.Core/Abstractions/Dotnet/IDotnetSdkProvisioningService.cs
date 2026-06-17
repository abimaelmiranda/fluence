using System;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Models.Dotnet;

namespace Fluence.Core.Abstractions.Dotnet;

public interface IDotnetSdkProvisioningService
{
    Task<DotnetSdkStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<string> ResolveDotnetExecutableAsync(CancellationToken cancellationToken = default);

    Task InstallIdeManagedSdkAsync(
        string channel,
        string displayName,
        Action<string> onOutput,
        CancellationToken cancellationToken = default);

    Task OpenSystemWideInstallerAsync(Action<string> onOutput, CancellationToken cancellationToken = default);
}
