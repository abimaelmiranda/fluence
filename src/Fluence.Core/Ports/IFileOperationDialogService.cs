using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Ports;

public interface IFileOperationDialogService
{
    Task<bool> ConfirmDeleteAsync(string path, bool isDirectory, CancellationToken cancellationToken = default);
}
