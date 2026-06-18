using Fluence.Core.Abstractions.Jobs;
using Fluence.Core.Models.Jobs;

namespace Fluence.Core.Services.Jobs;

public sealed class ExclusiveJobCoordinator : IExclusiveJobCoordinator
{
    private readonly object _gate = new();
    private Lease? _activeLease;

    public ExclusiveJobKind? ActiveJob
    {
        get
        {
            lock (_gate)
            {
                return _activeLease?.Kind;
            }
        }
    }

    public bool TryAcquire(ExclusiveJobKind kind, out IExclusiveJobLease? lease)
    {
        lock (_gate)
        {
            if (_activeLease is not null)
            {
                lease = null;
                return false;
            }

            _activeLease = new Lease(this, kind);
            lease = _activeLease;
            return true;
        }
    }

    private void Release(Lease lease)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_activeLease, lease))
                _activeLease = null;
        }
    }

    private sealed class Lease(ExclusiveJobCoordinator owner, ExclusiveJobKind kind) : IExclusiveJobLease
    {
        private bool _disposed;

        public ExclusiveJobKind Kind { get; } = kind;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            owner.Release(this);
        }
    }
}
