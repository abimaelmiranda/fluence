using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using AvaloniaEdit.TextMate;
using Fluence.Modules.Editor.ViewModels;
using TextMateSharp.Grammars;

namespace Fluence.Modules.Editor.Views;

public partial class EditorView : UserControl
{
    private static readonly Dictionary<string, string> LanguageScopeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "source.cs",
        [".csproj"] = "text.xml",
        [".props"] = "text.xml",
        [".targets"] = "text.xml",
        [".slnx"] = "text.xml",
        [".xml"] = "text.xml",
        [".axaml"] = "text.xml",
        [".xaml"] = "text.xml",
    };

    private bool _isUpdatingEditorText;
    private EditorViewModel? _viewModel;
    private RegistryOptions? _registryOptions;
    private TextMate.Installation? _textMateInstallation;

    public EditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        Editor.TextChanged += OnEditorTextChanged;
        Editor.LostFocus += OnEditorLostFocus;
        InitializeTextMate();
    }

    private void InitializeTextMate()
    {
        _registryOptions = new RegistryOptions(ThemeName.DarkPlus);
        _textMateInstallation = Editor.InstallTextMate(_registryOptions);
    }

    private void ApplyGrammarForPath(string? path)
    {
        if (_registryOptions is null || _textMateInstallation is null || string.IsNullOrEmpty(path))
            return;

        var scope = ResolveScopeName(path);
        if (!string.IsNullOrEmpty(scope))
            _textMateInstallation.SetGrammar(scope);
    }

    private string? ResolveScopeName(string path)
    {
        if (_registryOptions is null)
            return null;

        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
            return null;

        if (LanguageScopeByExtension.TryGetValue(extension, out var explicitScope))
            return explicitScope;

        var language = _registryOptions.GetLanguageByExtension(extension) ??
                       _registryOptions.GetLanguageByExtension(extension.TrimStart('.'));

        return language is null ? null : _registryOptions.GetScopeByLanguageId(language.Id);
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => BindViewModel();

    private void OnDataContextChanged(object? sender, System.EventArgs e) => BindViewModel();

    private void BindViewModel()
    {
        if (ReferenceEquals(_viewModel, DataContext))
        {
            if (_viewModel is not null)
                SetEditorText(_viewModel.ActiveText);
            return;
        }

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as EditorViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SetEditorText(_viewModel.ActiveText);
            ApplyGrammarForPath(_viewModel.ActiveDocumentPath);
        }
        else
        {
            SetEditorText(string.Empty);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_viewModel is null) return;

        if (e.PropertyName == nameof(EditorViewModel.ActiveText))
        {
            SetEditorText(_viewModel.ActiveText);
            ApplyGrammarForPath(_viewModel.ActiveDocumentPath);
        }
        else if (e.PropertyName == nameof(EditorViewModel.ActiveDocumentPath))
        {
            ApplyGrammarForPath(_viewModel.ActiveDocumentPath);
        }
    }

    private void OnEditorTextChanged(object? sender, System.EventArgs e)
    {
        if (_isUpdatingEditorText || _viewModel is null) return;
        _viewModel.ActiveText = Editor.Text;
    }

    private async void OnEditorLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel is not null)
            await _viewModel.SaveIfDirtyAsync();
    }

    private void SetEditorText(string text)
    {
        if (string.Equals(Editor.Text, text, System.StringComparison.Ordinal)) return;
        _isUpdatingEditorText = true;
        Editor.Text = text;
        _isUpdatingEditorText = false;
    }
}
