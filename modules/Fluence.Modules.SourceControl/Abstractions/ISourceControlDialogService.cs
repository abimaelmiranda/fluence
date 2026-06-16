using System.Threading;
using System.Threading.Tasks;
using Fluence.Modules.SourceControl.Models;

namespace Fluence.Modules.SourceControl.Abstractions;

public interface ISourceControlDialogService
{
    Task<bool> ConfirmRevertFileAsync(GitFileChange change, CancellationToken cancellationToken = default);
}
