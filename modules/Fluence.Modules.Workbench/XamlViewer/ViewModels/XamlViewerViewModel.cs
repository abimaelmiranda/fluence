using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.ViewModels;
using Fluence.Modules.Workbench.XamlViewer.Abstractions;

namespace Fluence.Modules.Workbench.XamlViewer.ViewModels;

public sealed partial class XamlViewerViewModel : ViewModelBase, IDisposable
{
    private readonly IXamlPreviewService _preview;
    private readonly IUiDispatcher _dispatcher;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    private string _sourceFileName = string.Empty;

    [ObservableProperty]
    private Control? _previewContent;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isHotReloadEnabled = true;

    public XamlViewerViewModel(IXamlPreviewService preview, IUiDispatcher dispatcher)
    {
        _preview = preview;
        _dispatcher = dispatcher;
    }

    public async Task LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            SourcePath = filePath;
            SourceFileName = Path.GetFileName(filePath);
        });

        await ExecuteAsync(token => File.ReadAllTextAsync(filePath, token), cancellationToken);
    }

    public Task LoadFromContentAsync(string xamlContent, CancellationToken cancellationToken = default) =>
        ExecuteAsync(_ => Task.FromResult(xamlContent), cancellationToken);

    private async Task ExecuteAsync(Func<CancellationToken, Task<string>> contentProvider, CancellationToken cancellationToken)
    {
        // Atomically swap the CTS so a concurrent call cancels the previous one without a race.
        var newCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var oldCts = Interlocked.Exchange(ref _cts, newCts);
        oldCts?.Cancel();
        oldCts?.Dispose();
        var token = newCts.Token;

        await _dispatcher.InvokeAsync(() =>
        {
            IsLoading = true;
            HasError = false;
            PreviewContent = null;
            ErrorMessage = string.Empty;
        });

        try
        {
            var xamlContent = await contentProvider(token);
            token.ThrowIfCancellationRequested();

            // Avalonia controls must be created on the UI thread; Render is synchronous.
            await _dispatcher.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                PreviewContent = _preview.Render(xamlContent, token);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await _dispatcher.InvokeAsync(() =>
            {
                HasError = true;
                ErrorMessage = ex.Message;
            });
        }
        finally
        {
            await _dispatcher.InvokeAsync(() => IsLoading = false);
            // Only clear the shared field if it still points to our CTS (not a newer one).
            Interlocked.CompareExchange(ref _cts, null, newCts);
            newCts.Dispose();
        }
    }

    public void Dispose()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        cts?.Cancel();
        cts?.Dispose();
    }
}
