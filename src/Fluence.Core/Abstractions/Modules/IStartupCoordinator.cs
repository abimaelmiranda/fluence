namespace Fluence.Core.Abstractions.Modules;

public interface IStartupCoordinator
{
    Task StartAsync(CancellationToken cancellationToken = default);
}
