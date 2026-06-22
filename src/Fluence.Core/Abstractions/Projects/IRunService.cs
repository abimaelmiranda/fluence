using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Abstractions.Projects;

public interface IRunService
{
    Task RunAsync(CancellationToken cancellationToken = default);

    Task RunSpecificAsync(string projectPath, CancellationToken cancellationToken = default);
}
