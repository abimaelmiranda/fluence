using System.Threading;
using Avalonia.Controls;

namespace Fluence.Modules.Workbench.XamlViewer.Abstractions;

public interface IXamlPreviewService
{
    Control? Render(string xamlContent, CancellationToken cancellationToken = default);
}
