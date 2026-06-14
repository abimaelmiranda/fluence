using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;
using Fluence.Core.Debug;
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
    private readonly DebugLineRenderer _debugLineRenderer = new();
    private CancellationTokenSource? _hoverCts;
    private CancellationTokenSource? _popupCloseCts;
    private bool _mouseInPopup;

    public EditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        Editor.TextChanged += OnEditorTextChanged;
        Editor.LostFocus += OnEditorLostFocus;
        Editor.PointerPressed += OnEditorPointerPressed;
        Editor.TextArea.TextView.BackgroundRenderers.Add(_debugLineRenderer);
        Editor.TextArea.TextView.PointerHover += OnPointerHover;
        Editor.TextArea.TextView.PointerHoverStopped += OnPointerHoverStopped;
        HoverPopupBorder.PointerEntered += (_, _) => { _mouseInPopup = true; _popupCloseCts?.Cancel(); };
        HoverPopupBorder.PointerExited += (_, _) => { _mouseInPopup = false; ClosePopupDelayed(); };
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
            UpdateDebugRendering();
        }
        else
        {
            SetEditorText(string.Empty);
            UpdateDebugRendering();
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
            UpdateDebugRendering();
        }
        else if (e.PropertyName == nameof(EditorViewModel.ActiveDocumentBreakpoints) ||
                 e.PropertyName == nameof(EditorViewModel.ActiveExecutionLine))
        {
            UpdateDebugRendering();
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

    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null)
            return;

        var textView = Editor.TextArea.TextView;
        var position = e.GetPosition(textView);
        if (position.X > 34)
            return;

        textView.EnsureVisualLines();
        var visualTop = position.Y + textView.ScrollOffset.Y;
        var visualLine = textView.VisualLines.FirstOrDefault(line =>
            visualTop >= line.VisualTop &&
            visualTop <= line.VisualTop + line.Height);
        if (visualLine is null)
            return;

        _viewModel.ToggleBreakpoint(visualLine.FirstDocumentLine.LineNumber);
        e.Handled = true;
    }

    private void OnPointerHover(object? sender, PointerEventArgs e)
    {
        if (_viewModel is null || !_viewModel.IsDebuggerStopped)
            return;

        var textView = Editor.TextArea.TextView;
        var pos = e.GetPosition(textView);
        var textPosition = textView.GetPosition(pos + textView.ScrollOffset);
        if (textPosition is null)
            return;

        var document = Editor.Document;
        if (document is null)
            return;

        var offset = document.GetOffset(textPosition.Value.Location);
        var word = ExtractWordAt(document, offset);
        if (string.IsNullOrWhiteSpace(word))
            return;

        var hoverPoint = e.GetPosition(EditorSurface);

        _hoverCts?.Cancel();
        _hoverCts?.Dispose();
        _hoverCts = new CancellationTokenSource();
        var token = _hoverCts.Token;

        _ = EvaluateAndShowAsync(word, hoverPoint, token);
    }

    private async System.Threading.Tasks.Task EvaluateAndShowAsync(string expression, Point hoverPoint, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(1500));

            var result = await _viewModel!.EvaluateHoverAsync(expression, timeout.Token).ConfigureAwait(false);
            if (result is null || cancellationToken.IsCancellationRequested)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                var node = new HoverVariableNode(result, _viewModel.GetChildVariablesAsync);
                HoverPopup.IsOpen = false;
                HoverTree.ItemsSource = new[] { node };
                HoverPopup.PlacementTarget = EditorSurface;
                HoverPopup.PlacementRect = new Rect(hoverPoint.X + 12, hoverPoint.Y + 18, 1, 1);
                HoverPopup.IsOpen = true;
            });
        }
        catch
        {
        }
    }

    private void OnPointerHoverStopped(object? sender, PointerEventArgs e)
    {
        _hoverCts?.Cancel();
        ClosePopupDelayed();
    }

    private void ClosePopupDelayed()
    {
        _popupCloseCts?.Cancel();
        _popupCloseCts?.Dispose();
        _popupCloseCts = new CancellationTokenSource();
        var token = _popupCloseCts.Token;
        _ = System.Threading.Tasks.Task.Delay(350, token).ContinueWith(_ =>
        {
            if (!token.IsCancellationRequested)
                Dispatcher.UIThread.Post(() => { if (!_mouseInPopup) HoverPopup.IsOpen = false; });
        }, System.Threading.Tasks.TaskScheduler.Default);
    }

    private static string ExtractWordAt(TextDocument document, int offset)
    {
        if (offset < 0 || offset >= document.TextLength)
            return string.Empty;

        var text = document.Text;
        var start = offset;
        while (start > 0 && IsWordChar(text[start - 1]))
            start--;

        var end = offset;
        while (end < text.Length && IsWordChar(text[end]))
            end++;

        return start < end ? text[start..end] : string.Empty;
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '.';

    private void UpdateDebugRendering()
    {
        _debugLineRenderer.Update(
            _viewModel?.ActiveDocumentBreakpoints ?? Array.Empty<DebugBreakpoint>(),
            _viewModel?.ActiveExecutionLine);
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private sealed class DebugLineRenderer : IBackgroundRenderer
    {
        private IReadOnlyList<DebugBreakpoint> _breakpoints = Array.Empty<DebugBreakpoint>();
        private DebugExecutionLine? _executionLine;

        public KnownLayer Layer => KnownLayer.Background;

        public void Update(IReadOnlyList<DebugBreakpoint> breakpoints, DebugExecutionLine? executionLine)
        {
            _breakpoints = breakpoints;
            _executionLine = executionLine;
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!textView.VisualLinesValid)
                return;

            foreach (var line in textView.VisualLines)
            {
                var lineNumber = line.FirstDocumentLine.LineNumber;
                if (_executionLine?.Line == lineNumber)
                {
                    var rect = new Rect(0, line.VisualTop - textView.ScrollOffset.Y, textView.Bounds.Width, line.Height);
                    drawingContext.FillRectangle(new SolidColorBrush(Color.FromArgb(42, 122, 92, 255)), rect);
                }

                var breakpoint = _breakpoints.FirstOrDefault(b => b.Line == lineNumber);
                if (breakpoint is not null)
                {
                    var center = new Point(18, line.VisualTop - textView.ScrollOffset.Y + line.Height / 2);
                    var brush = breakpoint.IsVerified
                        ? Brushes.IndianRed
                        : Brushes.DarkRed;
                    drawingContext.DrawEllipse(brush, null, center, 5, 5);
                }
            }
        }
    }
}
