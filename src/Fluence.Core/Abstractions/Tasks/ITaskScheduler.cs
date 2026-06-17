using System;
using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Abstractions.Tasks;

public interface ITaskScheduler
{
    void Schedule(
        string ownerId,
        TaskPriority priority,
        Func<CancellationToken, Task> work,
        object? correlationId = null);

    void ScheduleLatest(
        string ownerId,
        TaskPriority priority,
        TimeSpan delay,
        Func<CancellationToken, Task> work,
        object? correlationId = null);

    void Cancel(string ownerId);
}
