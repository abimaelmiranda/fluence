using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Abstractions.Commands;

public interface ICommandHandler<in TCommand>
{
    Task HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}
