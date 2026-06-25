using Avalonia.Interactivity;
using AvaloniaEdit.Rendering;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.LanguageServer;
using Fluence.Core.Events.Lsp;

namespace Fluence.Modules.Workbench.Editor.Views;

public partial class EditorView
{
    private void OnDiagnosticsUpdated(DiagnosticsUpdatedEvent e)
    {
        var activePath = _viewModel?.ActiveDocumentPath;
        if (!string.Equals(activePath, e.FilePath, StringComparison.OrdinalIgnoreCase))
            return;

        _diagnosticRenderer.Update(Editor.Document, e.Diagnostics);
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private void OnContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var hasLsp = _completionService is not null && _viewModel?.ActiveDocumentPath is not null;
        MenuItemIntelliSense.IsEnabled = hasLsp;
        MenuItemDuplicateLine.IsEnabled = Editor.Document is not null;
        MenuItemGoToDefinition.IsEnabled = hasLsp;
        MenuItemGoToImplementation.IsEnabled = hasLsp;
        MenuItemGoToTypeDefinition.IsEnabled = hasLsp;
    }

    private void OnMenuIntelliSense(object? sender, RoutedEventArgs e) =>
        _ = TriggerCompletionAsync(immediate: true);

    private void OnMenuDuplicateLine(object? sender, RoutedEventArgs e)
    {
        if (TryDuplicateSelectionOrLine())
            e.Handled = true;
    }

    private void OnMenuGoToDefinition(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var caret = Editor.TextArea.Caret;
        _viewModel.PublishGoToDefinition(caret.Line - 1, caret.Column - 1);
    }

    private void OnMenuGoToImplementation(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var caret = Editor.TextArea.Caret;
        _viewModel.PublishGoToImplementation(caret.Line - 1, caret.Column - 1);
    }

    private void OnMenuGoToTypeDefinition(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var caret = Editor.TextArea.Caret;
        _viewModel.PublishGoToTypeDefinition(caret.Line - 1, caret.Column - 1);
    }
}
