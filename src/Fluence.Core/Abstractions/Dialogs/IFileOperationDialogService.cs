using System.Threading;
using System.Threading.Tasks;

namespace Fluence.Core.Abstractions.Dialogs;

public interface IFileOperationDialogService
{
    Task<bool> ConfirmDeleteAsync(string path, bool isDirectory, CancellationToken cancellationToken = default);

    Task<string?> PromptForNameAsync(
        string title,
        string label,
        string? initialValue = null,
        CancellationToken cancellationToken = default);
}
