using System;
using Fluence.Core.Models.Jobs;

namespace Fluence.Core.Abstractions.Jobs;

public interface IExclusiveJobCoordinator
{
    ExclusiveJobKind? ActiveJob { get; }

    bool TryAcquire(ExclusiveJobKind kind, out IExclusiveJobLease? lease);
}

public interface IExclusiveJobLease : IDisposable
{
    ExclusiveJobKind Kind { get; }
}
