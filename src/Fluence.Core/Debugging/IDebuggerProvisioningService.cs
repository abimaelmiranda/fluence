using System;
using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Debug;

public interface IDebuggerProvisioningService
{
    bool IsProvisioned();

    string GetExecutablePath();

    Task ProvisionAsync(Action<string> onOutput, CancellationToken cancellationToken = default);
}
