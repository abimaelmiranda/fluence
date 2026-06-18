using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Abstractions.Infrastructure;

public interface IProcessHost
{
    Task RunAsync(
        string executable,
        string arguments,
        string? workingDirectory,
        Action<string> onOutput,
        Action<string> onError,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? environment = null);

    Task RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        Action<string> onOutput,
        Action<string> onError,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? environment = null);
}
