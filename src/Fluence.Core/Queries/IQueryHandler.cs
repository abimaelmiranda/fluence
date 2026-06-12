using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Queries;

public interface IQueryHandler<in TQuery, TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}
