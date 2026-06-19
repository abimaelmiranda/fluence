using System;
using System.Threading;
using System.Threading.Tasks;
using Fluence.Core.Models.Lifecycle;

namespace Fluence.Core.Abstractions.Lifecycle;

public interface IApplicationLifecycleService
{
    event EventHandler<ApplicationShutdownStatusChangedEventArgs>? StatusChanged;

    bool IsShutdownInProgress { get; }

    bool IsShutdownComplete { get; }

    Task<ApplicationShutdownOutcome> RequestShutdownAsync(
        ApplicationShutdownRequest request,
        CancellationToken cancellationToken = default);
}
