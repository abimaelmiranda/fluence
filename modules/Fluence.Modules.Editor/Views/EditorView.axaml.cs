using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.TextMate;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Models.LanguageServer;
using Fluence.Core.Services.Debugging;
using Fluence.Modules.Editor.ViewModels;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Themes.Reader;
using TextMateSharp.Themes;

namespace Fluence.Modules.Editor.Views;

public partial class EditorView : UserControl
{
    private static readonly TimeSpan CompletionDebounceDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan DotCompletionDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan HoverDebounceDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LspHoverDebounceDelay = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan CompletionRefreshDelay = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan PopupCloseDelay = TimeSpan.FromMilliseconds(350);

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
    private readonly DiagnosticRenderer _diagnosticRenderer = new();
    private readonly SemanticColorizer _semanticColorizer = new(); //TODO when adding the theme customization, this should be reworked to support dynamic theme changes.
    private BreakpointMargin? _breakpointMargin;
    private bool _mouseInPopup;
    private ICompletionService? _completionService;
    private IHoverService? _hoverService;
    private ISignatureHelpService? _signatureHelpService;
    private IShellEventBus? _eventBus;
    private List<LspCompletionData>? _activeCompletions;
    private readonly object _completionGate = new();
    private readonly object _hoverGate = new();
    private readonly object _lspHoverGate = new();
    private readonly object _popupCloseGate = new();
    private Timer? _completionTimer;
    private Timer? _hoverTimer;
    private Timer? _lspHoverTimer;
    private Timer? _popupCloseTimer;
    private DispatcherTimer? _completionRefreshTimer;
    private CompletionRequest? _pendingCompletionRequest;
    private HoverRequest? _pendingHoverRequest;
    private LspHoverRequest? _pendingLspHoverRequest;
    private int _completionRequestVersion;
    private int _hoverRequestVersion;
    private int _lspHoverRequestVersion;
    private int _signatureHelpVersion;
    private int _completionTriggerOffset = -1;
    private int _signatureTriggerOffset = -1;
    private bool _completionRequestInFlight;
    private bool _hoverRequestInFlight;
    private bool _lspHoverRequestInFlight;
    private string? _lastKnownDocumentPath;
    private bool _completionRefreshPending;
    private static readonly Dictionary<char, char> AutoPairClosers = new()
    {
        ['('] = ')',
        ['['] = ']',
        ['{'] = '}',
        ['"'] = '"',
        ['\''] = '\'',
    };
    private static readonly HashSet<char> AutoPairClosingChars = new(AutoPairClosers.Values);

    public EditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += OnAttachedToVisualTree;
        Editor.TextChanged += OnEditorTextChanged;
        Editor.LostFocus += OnEditorLostFocus;
        _breakpointMargin = new BreakpointMargin(line => _viewModel?.ToggleBreakpoint(line));
        Editor.TextArea.LeftMargins.Insert(0, _breakpointMargin);
        Editor.TextArea.AddHandler(
            InputElement.PointerPressedEvent,
            OnBreakpointAreaPressed,
            RoutingStrategies.Tunnel);
        Editor.TextArea.AddHandler(
            InputElement.PointerMovedEvent,
            OnBreakpointAreaPointerMoved,
            RoutingStrategies.Tunnel);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_debugLineRenderer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_diagnosticRenderer);
        Editor.TextArea.TextView.PointerHover += OnPointerHover;
        Editor.TextArea.TextView.PointerHoverStopped += OnPointerHoverStopped;
        HoverPopupBorder.PointerEntered += (_, _) => { _mouseInPopup = true; CancelPopupClose(); };
        HoverPopupBorder.PointerExited += (_, _) => { _mouseInPopup = false; ClosePopupDelayed(); };
        LspHoverBorder.PointerEntered += (_, _) => { _mouseInPopup = true; CancelPopupClose(); };
        LspHoverBorder.PointerExited += (_, _) => { _mouseInPopup = false; ClosePopupDelayed(); };
        Editor.TextArea.TextEntering += OnTextEntering;
        Editor.TextArea.TextEntered += OnTextEntered;
        Editor.AddHandler(KeyDownEvent, OnEditorPreviewKeyDown, RoutingStrategies.Tunnel, true);
        _completionRefreshTimer = new DispatcherTimer { Interval = CompletionRefreshDelay };
        _completionRefreshTimer.Tick += OnCompletionRefreshTimerTick;
        CompletionListBox.ItemTemplate = new FuncDataTemplate<LspCompletionData>(
            (data, _) => data is null ? new TextBlock() : (Control)data.Content,
            supportsRecycling: false);
        InitializeTextMate();
    }

    public void SetServices(
        ICompletionService completionService,
        IShellEventBus eventBus,
        IHoverService? hoverService = null,
        ISignatureHelpService? signatureHelpService = null)
    {
        _completionService = completionService;
        _eventBus = eventBus;
        _hoverService = hoverService;
        _signatureHelpService = signatureHelpService;

        eventBus.Subscribe<DiagnosticsUpdatedEvent>(OnDiagnosticsUpdated);
        eventBus.Subscribe<NavigationResolvedEvent>(OnNavigationResolved);
        eventBus.Subscribe<SemanticTokensUpdatedEvent>(OnSemanticTokensUpdated);
    }

    private void OnDiagnosticsUpdated(DiagnosticsUpdatedEvent e)
    {
        var activePath = _viewModel?.ActiveDocumentPath;
        if (!string.Equals(activePath, e.FilePath, StringComparison.OrdinalIgnoreCase))
            return;

        Dispatcher.UIThread.Post(() =>
        {
            _diagnosticRenderer.Update(Editor.Document, e.Diagnostics);
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }, DispatcherPriority.Background);
    }

    private void OnSemanticTokensUpdated(SemanticTokensUpdatedEvent e)
    {
        var activePath = _viewModel?.ActiveDocumentPath;
        if (!string.Equals(activePath, e.FilePath, StringComparison.OrdinalIgnoreCase))
            return;

        Dispatcher.UIThread.Post(() =>
        {
            _semanticColorizer.Update(e.Tokens);
            Editor.TextArea.TextView.Redraw();
        }, DispatcherPriority.Background);
    }

    private void OnNavigationResolved(NavigationResolvedEvent e) =>
        NavigateToLocation(e, retries: 0);

    private void NavigateToLocation(NavigationResolvedEvent e, int retries)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var doc = Editor.Document;
            if (doc is null) return;

            // Cross-file navigation: wait for the correct document to be loaded
            var loadedPath = _viewModel?.ActiveDocumentPath;
            if (!string.Equals(loadedPath, e.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                if (retries < 10)
                    Task.Delay(50).ContinueWith(_ => NavigateToLocation(e, retries + 1));
                return;
            }

            // LSP lines are 0-based; clamp final offset against doc length
            var targetLine = Math.Clamp(e.Line + 1, 1, doc.LineCount);
            var docLine = doc.GetLineByNumber(targetLine);
            var offset = Math.Clamp(
                docLine.Offset + Math.Clamp(e.Character, 0, docLine.Length),
                0, doc.TextLength);

            SetCaretOffset(offset);
            Editor.ScrollToLine(targetLine);
        }, DispatcherPriority.Background);
    }

    private void OnTextEntered(object? sender, Avalonia.Input.TextInputEventArgs e)
    {
        if (_viewModel?.ActiveDocumentPath is null)
            return;

        if (e.Text?.Length != 1)
            return;
        var ch = e.Text[0];

        // Signature help triggers
        if (ch == '(' || ch == ',')
            _ = TriggerSignatureHelpAsync(ch);
        else if (ch == ')')
            CloseSignatureHelpPopup();

        if (_completionService is null)
            return;
        if (ch != '.' && !char.IsLetter(ch) && ch != '_')
            return;

        // Dot while popup is open: close the existing generic popup and retrigger for member completion.
        if (ch == '.' && CompletionPopup.IsOpen)
            CloseCompletionPopup();

        if (CompletionPopup.IsOpen)
        {
            ScheduleCompletionWindowRefresh();
            return;
        }

        // For dot: flush LSP document sync immediately so OmniSharp has the latest content
        // (including the dot) before we request completions. Without this, the 750ms debounce
        // on didChange means OmniSharp would return generic completions instead of member completions.
        if (ch == '.' && _eventBus is not null)
            _eventBus.Publish(new FlushDocumentSyncEvent(_viewModel.ActiveDocumentPath));

        Dispatcher.UIThread.Post(() =>
        {
            if (_completionService is null || _viewModel?.ActiveDocumentPath is null)
                return;

            if (CompletionPopup.IsOpen)
            {
                ScheduleCompletionWindowRefresh();
                return;
            }

            _ = TriggerCompletionAsync(immediate: ch == '.');
        }, DispatcherPriority.Background);
    }

    private void OnTextEntering(object? sender, Avalonia.Input.TextInputEventArgs e)
    {
        if (e.Text?.Length != 1)
            return;

        var ch = e.Text[0];
        var document = Editor.Document;
        if (document is null)
            return;

        var textArea = Editor.TextArea;
        var selection = textArea.Selection;
        var caretOffset = textArea.Caret.Offset;

        if (!AutoPairClosers.TryGetValue(ch, out var closer))
        {
            if (!selection.IsEmpty || !AutoPairClosingChars.Contains(ch))
                return;

            if (IsInsideComment(document, caretOffset))
                return;

            if (caretOffset < document.TextLength && document.GetCharAt(caretOffset) == ch)
            {
                SetCaretOffset(caretOffset + 1);
                e.Handled = true;
            }

            return;
        }

        if (IsInsideComment(document, caretOffset))
            return;

        if (!selection.IsEmpty)
        {
            var selectionSegment = selection.SurroundingSegment;
            var selectedText = document.GetText(selectionSegment.Offset, selectionSegment.Length);
            var wrappedText = $"{ch}{selectedText}{closer}";
            document.Replace(selectionSegment.Offset, selectionSegment.Length, wrappedText);
            SetCaretOffset(selectionSegment.Offset + wrappedText.Length);
            e.Handled = true;
            return;
        }

        if (caretOffset < document.TextLength && document.GetCharAt(caretOffset) == closer)
        {
            SetCaretOffset(caretOffset + 1);
            e.Handled = true;
            return;
        }

        document.Insert(caretOffset, $"{ch}{closer}");
        SetCaretOffset(caretOffset + 1);
        e.Handled = true;
    }

    private Task TriggerCompletionAsync(bool immediate = false)
    {
        if (_completionService is null || _viewModel?.ActiveDocumentPath is null)
            return Task.CompletedTask;

        var caret = Editor.TextArea.Caret;
        var request = new CompletionRequest(
            _viewModel.ActiveDocumentPath,
            caret.Line - 1,
            caret.Column - 1,
            caret.Offset,
            Interlocked.Increment(ref _completionRequestVersion));

        ScheduleCompletionRequest(request, immediate ? DotCompletionDelay : CompletionDebounceDelay);
        return Task.CompletedTask;
    }

    private void ScheduleCompletionRequest(CompletionRequest request, TimeSpan delay)
    {
        lock (_completionGate)
        {
            _pendingCompletionRequest = request;
            _completionTimer ??= new Timer(
                static state => ((EditorView)state!).OnCompletionTimerElapsed(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            _completionTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnCompletionTimerElapsed()
    {
        CompletionRequest? request;
        lock (_completionGate)
        {
            if (_completionRequestInFlight || _pendingCompletionRequest is null)
                return;

            request = _pendingCompletionRequest;
            _pendingCompletionRequest = null;
            _completionRequestInFlight = true;
        }

        if (request is not null)
            _ = ProcessCompletionRequestAsync(request);
    }

    private async Task ProcessCompletionRequestAsync(CompletionRequest request)
    {
        try
        {
            if (_completionService is null ||
                _viewModel?.ActiveDocumentPath is null ||
                !string.Equals(_viewModel.ActiveDocumentPath, request.FilePath, StringComparison.OrdinalIgnoreCase) ||
                request.Version != Volatile.Read(ref _completionRequestVersion))
                return;

            var completions = await _completionService.GetCompletionsAsync(
                request.FilePath,
                request.Line,
                request.Character,
                CancellationToken.None).ConfigureAwait(false);

            if (request.Version != Volatile.Read(ref _completionRequestVersion) || completions.Count == 0)
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_completionService is null ||
                    _viewModel?.ActiveDocumentPath is null ||
                    !string.Equals(_viewModel.ActiveDocumentPath, request.FilePath, StringComparison.OrdinalIgnoreCase) ||
                    request.Version != Volatile.Read(ref _completionRequestVersion) ||
                    Editor.TextArea.Caret.Offset != request.CaretOffset)
                    return;

                CloseCompletionPopup();
                _activeCompletions = completions
                    .Where(item => item?.Label is not null)
                    .OrderBy(item => item.SortText is null ? 1 : 0)
                    .ThenBy(item => item.SortText, StringComparer.Ordinal)
                    .ThenBy(item => item.IsPreselected ? 0 : 1)
                    .Select((item, index) => new LspCompletionData(item, 100000 - index))
                    .ToList();
                var caretOffsetNow = Editor.TextArea.Caret.Offset;
                var currentPrefix = ExtractCompletionPrefix(Editor.Document, caretOffsetNow);
                _completionTriggerOffset = caretOffsetNow - currentPrefix.Length;
                Editor.TextArea.Caret.PositionChanged -= OnCaretPositionChangedForCompletion;
                Editor.TextArea.Caret.PositionChanged += OnCaretPositionChangedForCompletion;
                RefreshCompletionWindowItems();
                ShowCompletionPopup();
            });
        }
        catch
        {
        }
        finally
        {
            lock (_completionGate)
            {
                _completionRequestInFlight = false;
                if (_pendingCompletionRequest is not null)
                {
                    _completionTimer ??= new Timer(
                        static state => ((EditorView)state!).OnCompletionTimerElapsed(),
                        this,
                        Timeout.InfiniteTimeSpan,
                        Timeout.InfiniteTimeSpan);
                    _completionTimer.Change(CompletionDebounceDelay, Timeout.InfiniteTimeSpan);
                }
            }
        }
    }

    private void ScheduleCompletionWindowRefresh()
    {
        if (!CompletionPopup.IsOpen || _activeCompletions is null)
            return;

        _completionRefreshPending = true;
        _completionRefreshTimer?.Stop();
        _completionRefreshTimer?.Start();
    }

    private void OnCompletionRefreshTimerTick(object? sender, EventArgs e)
    {
        _completionRefreshTimer?.Stop();
        if (!_completionRefreshPending)
            return;

        _completionRefreshPending = false;
        RefreshCompletionWindowItems();
    }

    private void OnCaretPositionChangedForCompletion(object? sender, EventArgs e)
    {
        if (!CompletionPopup.IsOpen || _completionTriggerOffset < 0)
            return;
        if (Editor.TextArea.Caret.Offset < _completionTriggerOffset)
        {
            // Defer close by one UI cycle to avoid false positives from transient caret
            // repositioning that AvaloniaEdit may emit during visual layout (e.g. EnsureVisualLines).
            Dispatcher.UIThread.Post(() =>
            {
                if (CompletionPopup.IsOpen &&
                    _completionTriggerOffset >= 0 &&
                    Editor.TextArea.Caret.Offset < _completionTriggerOffset)
                    CloseCompletionPopup();
            }, DispatcherPriority.Background);
        }
    }

    private void ShowCompletionPopup()
    {
        var textView = Editor.TextArea.TextView;
        textView.EnsureVisualLines();
        var caretPos = Editor.TextArea.Caret.Position;
        // Guard: the caret line might not have a visual line if it's outside the rendered viewport.
        if (textView.GetVisualLine(caretPos.Line) is null)
            return;
        var visualBottom = textView.GetVisualPosition(caretPos, VisualYPosition.LineBottom);
        var scrollOffset = textView.ScrollOffset;
        // Use textView as PlacementTarget (same pattern as CompletionWindowBase in AvaloniaEdit).
        // PlacementRect is in textView's viewport coordinate space: document position minus scroll offset.
        CompletionPopup.PlacementTarget = textView;
        CompletionPopup.PlacementRect = new Rect(
            visualBottom.X - scrollOffset.X,
            visualBottom.Y - scrollOffset.Y,
            1, 1);
        CompletionPopup.IsOpen = true;
    }

    private void CloseCompletionPopup()
    {
        if (!CompletionPopup.IsOpen) return;
        Editor.TextArea.Caret.PositionChanged -= OnCaretPositionChangedForCompletion;
        CompletionPopup.IsOpen = false;
        _activeCompletions = null;
        _completionTriggerOffset = -1;
        CompletionListBox.ItemsSource = null;
    }

    private void CommitCompletion()
    {
        if (!CompletionPopup.IsOpen || _activeCompletions is null) return;
        var selected = CompletionListBox.SelectedItem as LspCompletionData;
        CloseCompletionPopup();
        if (selected is not null)
        {
            var segment = new AnchorSegment(Editor.Document, Editor.TextArea.Caret.Offset, 0);
            selected.Complete(Editor.TextArea, segment, EventArgs.Empty);
        }
    }

    private void OnEditorPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null) return;

        if (CompletionPopup.IsOpen)
        {
            if (e.Key == Key.Down)
            {
                var count = CompletionListBox.ItemCount;
                if (count > 0)
                    CompletionListBox.SelectedIndex = Math.Min(CompletionListBox.SelectedIndex + 1, count - 1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up)
            {
                if (CompletionListBox.SelectedIndex > 0)
                    CompletionListBox.SelectedIndex--;
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Enter || (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None))
            {
                CommitCompletion();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                CloseCompletionPopup();
                e.Handled = true;
                return;
            }
        }

        if (TryHandleSmartEnter(e))
        {
            e.Handled = true;
            return;
        }

        if (TryHandleMacEditingShortcut(e))
        {
            e.Handled = true;
            return;
        }

        var caret = Editor.TextArea.Caret;
        var line = caret.Line - 1;
        var character = caret.Column - 1;

        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Control)
        {
            _ = TriggerCompletionAsync(immediate: true);
            e.Handled = true;
        }
        else if (e.Key == Key.F12 && e.KeyModifiers == KeyModifiers.None)
        {
            _viewModel.PublishGoToDefinition(line, character);
            e.Handled = true;
        }
        else if (e.Key == Key.F12 && e.KeyModifiers == KeyModifiers.Control)
        {
            _viewModel.PublishGoToImplementation(line, character);
            e.Handled = true;
        }
        else if (e.Key == Key.F12 && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            _viewModel.PublishGoToTypeDefinition(line, character);
            e.Handled = true;
        }
    }

    private bool TryHandleMacEditingShortcut(KeyEventArgs e)
    {
        if (!OperatingSystem.IsMacOS())
            return false;

        var modifiers = e.KeyModifiers;
        var isCommand = modifiers == KeyModifiers.Meta;
        var isOption = modifiers == KeyModifiers.Alt;

        if (!isCommand && !isOption)
            return false;

        if (e.Key == Key.Back || e.Key == Key.Delete)
        {
            if (DeleteSelectionIfPresent())
                return true;

            if (isCommand)
            {
                DeleteCurrentLine();
                return true;
            }

            if (isOption || modifiers == KeyModifiers.Control)
            {
                DeleteWordBackward();

                return true;
            }
        }

        if (isCommand && (e.Key == Key.Left || e.Key == Key.Right))
        {
            MoveCaretToLineBoundary(e.Key == Key.Left);
            return true;
        }

        if (isOption && (e.Key == Key.Left || e.Key == Key.Right))
        {
            MoveCaretByWord(e.Key == Key.Left);
            return true;
        }

        return false;
    }

    private bool DeleteSelectionIfPresent()
    {
        var document = Editor.Document;
        if (document is null)
            return false;

        var selection = Editor.TextArea.Selection;
        if (selection.IsEmpty)
            return false;

        document.Remove(selection.SurroundingSegment.Offset, selection.SurroundingSegment.Length);
        SetCaretOffset(selection.SurroundingSegment.Offset);
        return true;
    }

    private void DeleteCurrentLine()
    {
        var document = Editor.Document;
        if (document is null)
            return;

        var caretLine = Editor.TextArea.Caret.Line;
        if (caretLine <= 0 || caretLine > document.LineCount)
            return;

        var line = document.GetLineByNumber(caretLine);
        var deleteStart = line.Offset;
        var deleteEnd = Editor.TextArea.Caret.Offset;

        if (deleteEnd <= deleteStart)
        {
            if (caretLine <= 1)
                return;

            var previousLine = document.GetLineByNumber(caretLine - 1);
            Editor.TextArea.Caret.Offset = previousLine.EndOffset;
            return;
        }

        document.Remove(deleteStart, deleteEnd - deleteStart);
        SetCaretOffset(deleteStart);
    }

    private void DeleteWordBackward()
    {
        var document = Editor.Document;
        if (document is null)
            return;

        var caretOffset = Editor.TextArea.Caret.Offset;
        var deleteStart = FindPreviousWordStart(document, caretOffset);

        if (deleteStart >= caretOffset)
            return;

        document.Remove(deleteStart, caretOffset - deleteStart);
        SetCaretOffset(deleteStart);
    }

    private void MoveCaretToLineBoundary(bool toStart)
    {
        var document = Editor.Document;
        if (document is null)
            return;

        var caretOffset = Editor.TextArea.Caret.Offset;
        var line = document.GetLineByOffset(caretOffset);
        SetCaretOffset(toStart ? line.Offset : line.EndOffset);
    }

    private void MoveCaretByWord(bool toStart)
    {
        var document = Editor.Document;
        if (document is null)
            return;

        var caretOffset = Editor.TextArea.Caret.Offset;
        SetCaretOffset(toStart
            ? FindPreviousWordStart(document, caretOffset)
            : FindNextWordEnd(document, caretOffset));
    }

    private void SetCaretOffset(int offset)
    {
        Editor.TextArea.Caret.Offset = offset;
        Editor.TextArea.Caret.BringCaretToView();
    }

    private static int FindPreviousWordStart(TextDocument document, int offset)
    {
        var text = document.Text;
        var index = Math.Clamp(offset, 0, text.Length);

        while (index > 0 && char.IsWhiteSpace(text[index - 1]))
            index--;

        if (index > 0)
        {
            var isWord = IsWordCharacter(text[index - 1]);
            while (index > 0 && IsWordCharacter(text[index - 1]) == isWord)
                index--;
        }

        return index;
    }

    private static int FindNextWordEnd(TextDocument document, int offset)
    {
        var text = document.Text;
        var index = Math.Clamp(offset, 0, text.Length);

        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;

        while (index < text.Length && IsWordCharacter(text[index]))
            index++;

        return index;
    }

    private static bool IsWordCharacter(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_';

    private bool TryHandleSmartEnter(KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None)
            return false;

        var document = Editor.Document;
        var activePath = _viewModel?.ActiveDocumentPath;
        if (document is null || string.IsNullOrWhiteSpace(activePath))
            return false;

        if (!string.Equals(Path.GetExtension(activePath), ".cs", StringComparison.OrdinalIgnoreCase))
            return false;

        var textArea = Editor.TextArea;
        if (!textArea.Selection.IsEmpty)
            return false;

        if (CompletionPopup.IsOpen)
            return false;

        var caretOffset = textArea.Caret.Offset;
        if (IsInsideComment(document, caretOffset))
            return false;

        var line = document.GetLineByOffset(caretOffset);
        var lineText = document.GetText(line.Offset, line.Length);
        var linePrefix = document.GetText(line.Offset, caretOffset - line.Offset);
        var lineSuffix = document.GetText(caretOffset, line.EndOffset - caretOffset);

        var indent = GetLineIndent(lineText);
        var baseIndent = indent;
        var indentUnit = GetIndentUnit(indent);

        var trimmedPrefix = linePrefix.TrimEnd();
        var trimmedSuffix = lineSuffix.TrimStart();

        if (trimmedPrefix.EndsWith("{", StringComparison.Ordinal) &&
            trimmedSuffix.StartsWith("}", StringComparison.Ordinal))
        {
            var openBraceRelativeOffset = linePrefix.LastIndexOf('{');
            if (openBraceRelativeOffset < 0)
                return false;

            var openBraceOffset = line.Offset + openBraceRelativeOffset;
            var replacement = string.Concat(
                Environment.NewLine,
                indent,
                "{",
                Environment.NewLine,
                indent,
                indentUnit,
                Environment.NewLine,
                indent,
                "}");

            document.Replace(openBraceOffset, caretOffset + 1 - openBraceOffset, replacement);
            SetCaretOffset(openBraceOffset + Environment.NewLine.Length + indent.Length + 1 + Environment.NewLine.Length + indent.Length + indentUnit.Length);
            return true;
        }

        if (trimmedPrefix.EndsWith("{", StringComparison.Ordinal))
        {
            var text = $"{Environment.NewLine}{indent}{indentUnit}";
            document.Insert(caretOffset, text);
            SetCaretOffset(caretOffset + text.Length);
            return true;
        }

        if (lineText.TrimStart().StartsWith("}", StringComparison.Ordinal) &&
            caretOffset <= line.Offset + (lineText.Length - lineText.TrimStart().Length))
        {
            var dedented = DedentIndent(baseIndent, indentUnit);
            var text = $"{Environment.NewLine}{dedented}";
            document.Insert(caretOffset, text);
            SetCaretOffset(caretOffset + text.Length);
            return true;
        }

        return false;
    }

    private static string GetLineIndent(string lineText)
    {
        var index = 0;
        while (index < lineText.Length && char.IsWhiteSpace(lineText[index]))
            index++;

        return index > 0 ? lineText[..index] : string.Empty;
    }

    private static string GetIndentUnit(string currentIndent) =>
        currentIndent.Contains('\t') ? "\t" : "    ";

    private static string DedentIndent(string currentIndent, string indentUnit)
    {
        if (string.IsNullOrEmpty(currentIndent))
            return string.Empty;

        if (currentIndent.EndsWith(indentUnit, StringComparison.Ordinal))
            return currentIndent[..^indentUnit.Length];

        if (currentIndent.EndsWith("\t", StringComparison.Ordinal))
            return currentIndent[..^1];

        var spacesToRemove = Math.Min(4, currentIndent.Length);
        return spacesToRemove > 0 ? currentIndent[..^spacesToRemove] : string.Empty;
    }

    private static bool IsInsideComment(TextDocument document, int caretOffset)
    {
        if (caretOffset <= 0)
            return false;

        var currentLine = document.GetLineByOffset(caretOffset);
        var inBlockComment = false;

        for (var lineNumber = 1; lineNumber <= currentLine.LineNumber; lineNumber++)
        {
            var line = document.GetLineByNumber(lineNumber);
            var lineText = document.GetText(line.Offset, line.Length);
            var limit = lineNumber == currentLine.LineNumber
                ? Math.Min(caretOffset - line.Offset, lineText.Length)
                : lineText.Length;

            for (var i = 0; i < limit; i++)
            {
                var ch = lineText[i];
                var next = i + 1 < limit ? lineText[i + 1] : '\0';

                if (inBlockComment)
                {
                    if (ch == '*' && next == '/')
                    {
                        inBlockComment = false;
                        i++;
                    }

                    continue;
                }

                if (ch == '/' && next == '/')
                    break;

                if (ch == '/' && next == '*')
                {
                    inBlockComment = true;
                    i++;
                    break;
                }
            }
        }

        return inBlockComment;
    }

    private void OnContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var hasLsp = _completionService is not null && _viewModel?.ActiveDocumentPath is not null;
        MenuItemIntelliSense.IsEnabled = hasLsp;
        MenuItemGoToDefinition.IsEnabled = hasLsp;
        MenuItemGoToImplementation.IsEnabled = hasLsp;
        MenuItemGoToTypeDefinition.IsEnabled = hasLsp;
    }

    private void OnMenuIntelliSense(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        _ = TriggerCompletionAsync(immediate: true);

    private void OnMenuGoToDefinition(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var caret = Editor.TextArea.Caret;
        _viewModel.PublishGoToDefinition(caret.Line - 1, caret.Column - 1);
    }

    private void OnMenuGoToImplementation(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var caret = Editor.TextArea.Caret;
        _viewModel.PublishGoToImplementation(caret.Line - 1, caret.Column - 1);
    }

    private void OnMenuGoToTypeDefinition(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        var caret = Editor.TextArea.Caret;
        _viewModel.PublishGoToTypeDefinition(caret.Line - 1, caret.Column - 1);
    }

    private void InitializeTextMate()
    {
        _registryOptions = new RegistryOptions(ThemeName.VisualStudioDark);
        _textMateInstallation = Editor.InstallTextMate(_registryOptions);

        var theme = LoadStandardTheme();
        if (theme is not null)
            _textMateInstallation.SetTheme(theme);

        // Must be registered AFTER TextMate so semantic colors override grammar colors.
        Editor.TextArea.TextView.LineTransformers.Add(_semanticColorizer);
    }

    private static IRawTheme? LoadStandardTheme()
    {
        // Hardcoded default theme. Later we'll expose an API to set custom themes and load from disk. 
        var uri = new Uri("avares://Fluence.Modules.Editor/Assets/Themes/standard_dark.json");

        using var stream = AssetLoader.Open(uri);
        using var reader = new StreamReader(stream);
        return ThemeReader.ReadThemeSync(reader);
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

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        BindViewModel();
        var lnm = Editor.TextArea.LeftMargins.OfType<LineNumberMargin>().FirstOrDefault();
        if (lnm is not null)
        {
            var idx = Editor.TextArea.LeftMargins.IndexOf(lnm);
            if (idx + 1 >= Editor.TextArea.LeftMargins.Count ||
                Editor.TextArea.LeftMargins[idx + 1] is not Border)
                Editor.TextArea.LeftMargins.Insert(idx + 1, new Border { Width = 6 });
        }
    }

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
            _lastKnownDocumentPath = _viewModel.ActiveDocumentPath;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SetEditorText(_viewModel.ActiveText);
            ApplyGrammarForPath(_viewModel.ActiveDocumentPath);
            UpdateDebugRendering();

            // Wire LSP services if available
            if (_completionService is null && _viewModel.CompletionService is not null)
                SetServices(_viewModel.CompletionService, _viewModel.EventBus,
                    _viewModel.HoverService, _viewModel.SignatureHelpService);
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
            var newPath = _viewModel.ActiveDocumentPath;
            if (string.Equals(newPath, _lastKnownDocumentPath, StringComparison.OrdinalIgnoreCase))
            {
                // Path unchanged — RefreshFromWorkspace fires this after every save.
                // Skip popup close and completion invalidation to avoid killing active completion.
                ApplyGrammarForPath(newPath);
                UpdateDebugRendering();
                return;
            }
            _lastKnownDocumentPath = newPath;
            InvalidateHoverRequests();
            Interlocked.Increment(ref _completionRequestVersion);
            Dispatcher.UIThread.Post(() =>
            {
                CloseCompletionPopup();
                CloseSignatureHelpPopup();
                HoverPopup.IsOpen = false;
                LspHoverPopup.IsOpen = false;
            });
            ApplyGrammarForPath(newPath);
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

    private void OnPointerHover(object? sender, PointerEventArgs e)
    {
        if (_viewModel is null)
            return;

        var textView = Editor.TextArea.TextView;
        var pos = e.GetPosition(textView);
        var textPosition = textView.GetPosition(pos + textView.ScrollOffset);
        if (textPosition is null)
            return;

        var document = Editor.Document;
        if (document is null)
            return;

        var hoverPoint = e.GetPosition(EditorSurface);

        if (_viewModel.IsDebuggerStopped)
        {
            var offset = document.GetOffset(textPosition.Value.Location);
            var word = ExtractWordAt(document, offset);
            if (string.IsNullOrWhiteSpace(word))
                return;
            CancelPopupClose();
            ScheduleHoverRequest(word, hoverPoint);
        }
        else
        {
            var hoveredLine = textPosition.Value.Line - 1;
            var hoveredChar = textPosition.Value.Column - 1;
            var diag = _diagnosticRenderer.FindDiagnosticAt(hoveredLine, hoveredChar);
            if (diag is not null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var isError = diag.Severity == LspDiagnosticSeverity.Error;
                    var prefix = isError ? "Error" : "Warning";
                    DiagnosticTooltipText.Text = $"[{prefix}] {diag.Message}";
                    DiagnosticTooltipText.Foreground = new SolidColorBrush(
                        isError ? Color.FromRgb(255, 144, 144) : Color.FromRgb(255, 208, 100));
                    DiagnosticTooltipBorder.BorderBrush = new SolidColorBrush(
                        isError ? Color.FromRgb(90, 32, 32) : Color.FromRgb(90, 74, 0));
                    DiagnosticTooltipPopup.PlacementTarget = EditorSurface;
                    DiagnosticTooltipPopup.PlacementRect = new Rect(hoverPoint.X + 12, hoverPoint.Y + 18, 1, 1);
                    DiagnosticTooltipPopup.IsOpen = true;
                });
                return;
            }

            if (_hoverService is not null)
                ScheduleLspHoverRequest(hoveredLine, hoveredChar, hoverPoint);
        }
    }

    private void ScheduleHoverRequest(string expression, Point hoverPoint)
    {
        var activePath = _viewModel?.ActiveDocumentPath;
        if (string.IsNullOrWhiteSpace(activePath))
            return;

        var request = new HoverRequest(
            activePath,
            expression,
            hoverPoint,
            Interlocked.Increment(ref _hoverRequestVersion));

        lock (_hoverGate)
        {
            _pendingHoverRequest = request;
            _hoverTimer ??= new Timer(
                static state => ((EditorView)state!).OnHoverTimerElapsed(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            _hoverTimer.Change(HoverDebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnHoverTimerElapsed()
    {
        HoverRequest? request;
        lock (_hoverGate)
        {
            if (_hoverRequestInFlight || _pendingHoverRequest is null)
                return;

            request = _pendingHoverRequest;
            _pendingHoverRequest = null;
            _hoverRequestInFlight = true;
        }

        if (request is not null)
            _ = EvaluateAndShowAsync(request);
    }

    private async Task EvaluateAndShowAsync(HoverRequest request)
    {
        try
        {
            if (_viewModel is null ||
                _viewModel.ActiveDocumentPath is null ||
                !string.Equals(_viewModel.ActiveDocumentPath, request.FilePath, StringComparison.OrdinalIgnoreCase) ||
                request.Version != Volatile.Read(ref _hoverRequestVersion))
                return;

            var result = await _viewModel.EvaluateHoverAsync(request.Expression, CancellationToken.None).ConfigureAwait(false);
            if (result is null ||
                _viewModel.ActiveDocumentPath is null ||
                !string.Equals(_viewModel.ActiveDocumentPath, request.FilePath, StringComparison.OrdinalIgnoreCase) ||
                request.Version != Volatile.Read(ref _hoverRequestVersion))
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_viewModel is null ||
                    _viewModel.ActiveDocumentPath is null ||
                    !string.Equals(_viewModel.ActiveDocumentPath, request.FilePath, StringComparison.OrdinalIgnoreCase) ||
                    request.Version != Volatile.Read(ref _hoverRequestVersion))
                    return;

                var node = new HoverVariableNode(result, _viewModel.GetChildVariablesAsync);
                HoverPopup.IsOpen = false;
                HoverTree.ItemsSource = new[] { node };
                HoverPopup.PlacementTarget = EditorSurface;
                HoverPopup.PlacementRect = new Rect(request.HoverPoint.X + 12, request.HoverPoint.Y + 18, 1, 1);
                HoverPopup.IsOpen = true;
            });
        }
        catch
        {
        }
        finally
        {
            lock (_hoverGate)
            {
                _hoverRequestInFlight = false;
                if (_pendingHoverRequest is not null)
                {
                    _hoverTimer ??= new Timer(
                        static state => ((EditorView)state!).OnHoverTimerElapsed(),
                        this,
                        Timeout.InfiniteTimeSpan,
                        Timeout.InfiniteTimeSpan);
                    _hoverTimer.Change(HoverDebounceDelay, Timeout.InfiniteTimeSpan);
                }
            }
        }
    }

    private void OnPointerHoverStopped(object? sender, PointerEventArgs e)
    {
        InvalidateHoverRequests();
        Interlocked.Increment(ref _lspHoverRequestVersion);
        lock (_lspHoverGate)
        {
            _pendingLspHoverRequest = null;
        }
        DiagnosticTooltipPopup.IsOpen = false;
        ClosePopupDelayed();
    }

    private void InvalidateHoverRequests()
    {
        Interlocked.Increment(ref _hoverRequestVersion);
        lock (_hoverGate)
        {
            _pendingHoverRequest = null;
        }
    }

    private void ClosePopupDelayed()
    {
        lock (_popupCloseGate)
        {
            _popupCloseTimer ??= new Timer(
                static state => ((EditorView)state!).OnPopupCloseTimerElapsed(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            _popupCloseTimer.Change(PopupCloseDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void CancelPopupClose()
    {
        lock (_popupCloseGate)
        {
            _popupCloseTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnPopupCloseTimerElapsed()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_mouseInPopup)
            {
                HoverPopup.IsOpen = false;
                LspHoverPopup.IsOpen = false;
                DiagnosticTooltipPopup.IsOpen = false;
            }
        });
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

    // ── LSP Hover ──────────────────────────────────────────────────────────

    private void ScheduleLspHoverRequest(int line, int character, Point hoverPoint)
    {
        var activePath = _viewModel?.ActiveDocumentPath;
        if (string.IsNullOrWhiteSpace(activePath))
            return;

        var request = new LspHoverRequest(
            activePath, line, character, hoverPoint,
            Interlocked.Increment(ref _lspHoverRequestVersion));

        lock (_lspHoverGate)
        {
            _pendingLspHoverRequest = request;
            _lspHoverTimer ??= new Timer(
                static state => ((EditorView)state!).OnLspHoverTimerElapsed(),
                this,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);
            _lspHoverTimer.Change(LspHoverDebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnLspHoverTimerElapsed()
    {
        LspHoverRequest? request;
        lock (_lspHoverGate)
        {
            if (_lspHoverRequestInFlight || _pendingLspHoverRequest is null)
                return;
            request = _pendingLspHoverRequest;
            _pendingLspHoverRequest = null;
            _lspHoverRequestInFlight = true;
        }

        if (request is not null)
            _ = ProcessLspHoverAsync(request);
    }

    private async Task ProcessLspHoverAsync(LspHoverRequest request)
    {
        try
        {
            if (_hoverService is null ||
                !string.Equals(_viewModel?.ActiveDocumentPath, request.FilePath, StringComparison.OrdinalIgnoreCase) ||
                request.Version != Volatile.Read(ref _lspHoverRequestVersion))
                return;

            var hover = await _hoverService.GetHoverAsync(
                request.FilePath, request.Line, request.Character, CancellationToken.None).ConfigureAwait(false);

            if (hover is null ||
                !string.Equals(_viewModel?.ActiveDocumentPath, request.FilePath, StringComparison.OrdinalIgnoreCase) ||
                request.Version != Volatile.Read(ref _lspHoverRequestVersion))
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!string.Equals(_viewModel?.ActiveDocumentPath, request.FilePath, StringComparison.OrdinalIgnoreCase) ||
                    request.Version != Volatile.Read(ref _lspHoverRequestVersion))
                    return;

                LspHoverText.Text = hover.Contents;
                LspHoverPopup.PlacementTarget = EditorSurface;
                LspHoverPopup.PlacementRect = new Rect(request.HoverPoint.X + 12, request.HoverPoint.Y + 18, 1, 1);
                LspHoverPopup.IsOpen = true;
            });
        }
        catch { }
        finally
        {
            lock (_lspHoverGate)
            {
                _lspHoverRequestInFlight = false;
                if (_pendingLspHoverRequest is not null)
                    _lspHoverTimer?.Change(LspHoverDebounceDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    // ── Signature Help ─────────────────────────────────────────────────────

    private async Task TriggerSignatureHelpAsync(char triggerChar)
    {
        if (_signatureHelpService is null || _viewModel?.ActiveDocumentPath is null)
            return;

        var caret = Editor.TextArea.Caret;
        var filePath = _viewModel.ActiveDocumentPath;
        var position = caret.Position;
        var line = position.Line - 1;
        var character = position.Column - 1;
        var caretOffset = caret.Offset;
        var isRetrigger = triggerChar == ',';
        var version = Interlocked.Increment(ref _signatureHelpVersion);

        if (!isRetrigger)
        {
            _signatureTriggerOffset = caretOffset;
            Editor.TextArea.Caret.PositionChanged -= OnCaretPositionChangedForSignatureHelp;
            Editor.TextArea.Caret.PositionChanged += OnCaretPositionChangedForSignatureHelp;
        }

        try
        {
            var sigHelp = await _signatureHelpService.GetSignatureHelpAsync(
                filePath, line, character, isRetrigger, CancellationToken.None).ConfigureAwait(false);

            if (version != Volatile.Read(ref _signatureHelpVersion))
                return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version != Volatile.Read(ref _signatureHelpVersion))
                    return;

                if (sigHelp is null || sigHelp.Signatures.Count == 0)
                {
                    CloseSignatureHelpPopup();
                    return;
                }

                RenderSignatureHelp(sigHelp);
            });
        }
        catch { }
    }

    private void OnCaretPositionChangedForSignatureHelp(object? sender, EventArgs e)
    {
        if (!SignatureHelpPopup.IsOpen || _signatureTriggerOffset < 0)
            return;
        if (Editor.TextArea.Caret.Offset < _signatureTriggerOffset)
            Dispatcher.UIThread.Post(CloseSignatureHelpPopup, DispatcherPriority.Background);
    }

    private void CloseSignatureHelpPopup()
    {
        if (!SignatureHelpPopup.IsOpen) return;
        Editor.TextArea.Caret.PositionChanged -= OnCaretPositionChangedForSignatureHelp;
        SignatureHelpPopup.IsOpen = false;
        _signatureTriggerOffset = -1;
        Interlocked.Increment(ref _signatureHelpVersion);
    }

    private void RenderSignatureHelp(LspSignatureHelp sigHelp)
    {
        var sigIndex = Math.Clamp(sigHelp.ActiveSignature, 0, sigHelp.Signatures.Count - 1);
        var sig = sigHelp.Signatures[sigIndex];
        var activeParam = sigHelp.ActiveParameter;
        var parameters = sig.Parameters;

        SignatureHelpText.Inlines?.Clear();

        if (parameters is null || parameters.Count == 0 || activeParam < 0 || activeParam >= parameters.Count)
            SignatureHelpText.Inlines?.Add(new Run(sig.Label));
        else
            BuildSignatureInlines(sig, activeParam);

        PositionSignatureHelpPopup();
        SignatureHelpPopup.IsOpen = true;
    }

    private void BuildSignatureInlines(LspSignatureInformation sig, int activeParamIndex)
    {
        var inlines = SignatureHelpText.Inlines;
        if (inlines is null) return;
        inlines.Clear();

        var label = sig.Label;
        var activeParam = sig.Parameters![activeParamIndex];

        int paramStart, paramEnd;
        if (activeParam.LabelStart.HasValue && activeParam.LabelEnd.HasValue)
        {
            paramStart = activeParam.LabelStart.Value;
            paramEnd = activeParam.LabelEnd.Value;
        }
        else if (!string.IsNullOrEmpty(activeParam.Label))
        {
            paramStart = label.IndexOf(activeParam.Label, StringComparison.Ordinal);
            paramEnd = paramStart >= 0 ? paramStart + activeParam.Label.Length : -1;
        }
        else
        {
            paramStart = paramEnd = -1;
        }

        var gray = new SolidColorBrush(Color.Parse("#AAAACC"));
        var white = new SolidColorBrush(Colors.White);

        if (paramStart < 0 || paramEnd <= paramStart || paramStart >= label.Length)
        {
            inlines.Add(new Run(label) { Foreground = gray });
            return;
        }

        paramEnd = Math.Min(paramEnd, label.Length);

        if (paramStart > 0)
            inlines.Add(new Run(label[..paramStart]) { Foreground = gray });

        inlines.Add(new Run(label[paramStart..paramEnd]) { Foreground = white, FontWeight = FontWeight.Bold });

        if (paramEnd < label.Length)
            inlines.Add(new Run(label[paramEnd..]) { Foreground = gray });
    }

    private void PositionSignatureHelpPopup()
    {
        var textView = Editor.TextArea.TextView;
        textView.EnsureVisualLines();
        var caretPos = Editor.TextArea.Caret.Position;
        if (textView.GetVisualLine(caretPos.Line) is null)
            return;

        var visualPos = textView.GetVisualPosition(caretPos, VisualYPosition.LineTop);
        var scrollOffset = textView.ScrollOffset;
        SignatureHelpPopup.PlacementTarget = textView;
        SignatureHelpPopup.PlacementRect = new Rect(
            visualPos.X - scrollOffset.X,
            visualPos.Y - scrollOffset.Y,
            1, 1);
    }

    private static string ExtractCompletionPrefix(TextDocument? document, int offset)
    {
        if (document is null || offset <= 0 || document.TextLength == 0)
            return string.Empty;

        var text = document.Text;
        var index = Math.Clamp(offset, 0, text.Length);

        while (index > 0 && IsCompletionChar(text[index - 1]))
            index--;

        return index < offset ? text[index..offset] : string.Empty;
    }

    private static bool IsCompletionChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_';

    private void RefreshCompletionWindowIfNeeded()
    {
        if (!CompletionPopup.IsOpen || _activeCompletions is null)
            return;

        RefreshCompletionWindowItems();
    }

    private sealed record CompletionRequest(string FilePath, int Line, int Character, int CaretOffset, int Version);

    private sealed record HoverRequest(string FilePath, string Expression, Point HoverPoint, int Version);

    private sealed record LspHoverRequest(string FilePath, int Line, int Character, Point HoverPoint, int Version);

    private void RefreshCompletionWindowItems()
    {
        if (_activeCompletions is null)
            return;

        var prefix = ExtractCompletionPrefix(Editor.Document, Editor.TextArea.Caret.Offset);
        var filtered = string.IsNullOrWhiteSpace(prefix)
            ? _activeCompletions
            : _activeCompletions.Where(item => item.MatchesPrefix(prefix)).ToList();

        if (filtered.Count == 0)
        {
            CloseCompletionPopup();
            return;
        }

        CompletionListBox.ItemsSource = filtered;
        CompletionListBox.SelectedIndex = 0;
    }

    private void UpdateDebugRendering()
    {
        var bps = _viewModel?.ActiveDocumentBreakpoints ?? Array.Empty<DebugBreakpoint>();
        _breakpointMargin?.Update(bps);
        _debugLineRenderer.Update(_viewModel?.ActiveExecutionLine);
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    private void OnBreakpointAreaPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_breakpointMargin is null) return;
        var posInMargin = e.GetPosition(_breakpointMargin);
        if (posInMargin.X < 0 || posInMargin.X > _breakpointMargin.Bounds.Width || posInMargin.Y < 0)
        {
            _breakpointMargin.SetHoveredLine(-1);
            return;
        }
        var textView = Editor.TextArea.TextView;
        if (!textView.VisualLinesValid) return;
        var vt = posInMargin.Y + textView.ScrollOffset.Y;
        var vl = textView.VisualLines.FirstOrDefault(l =>
            vt >= l.VisualTop && vt <= l.VisualTop + l.Height);
        _breakpointMargin.SetHoveredLine(vl?.FirstDocumentLine.LineNumber ?? -1);
    }

    private void OnBreakpointAreaPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null || _breakpointMargin is null) return;
        var posInMargin = e.GetPosition(_breakpointMargin);
        if (posInMargin.X < 0 || posInMargin.X > _breakpointMargin.Bounds.Width ||
            posInMargin.Y < 0)
            return;

        var textView = Editor.TextArea.TextView;
        textView.EnsureVisualLines();
        var scrollY = textView.ScrollOffset.Y;
        if (_breakpointMargin.TryToggleAtY(posInMargin.Y, scrollY, line =>
            {
                _viewModel.ToggleBreakpoint(line);
                return true;
            }))
            e.Handled = true;
    }

    private sealed class LspCompletionData
    {
        private readonly LspCompletion _completion;

        public LspCompletionData(LspCompletion completion, double priority)
        {
            _completion = completion;
            Priority = priority;
            Text = completion.Label;
        }

        public string Text { get; }
        public object Content => CreateContent(_completion);
        public double Priority { get; }

        public bool MatchesPrefix(string prefix) =>
            _completion.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            (_completion.InsertText ?? _completion.Label).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(_completion.SortText) &&
             _completion.SortText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        public void Complete(AvaloniaEdit.Editing.TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            var text = _completion.InsertText ?? _completion.Label;
            if (_completion.IsSnippet && _completion.InsertText is not null)
            {
                // Strip LSP snippet placeholders for basic insertion
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\$\{?\d+:?([^}]*)?\}?|\$0", m =>
                    m.Groups[1].Success ? m.Groups[1].Value : string.Empty);
            }
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^<>]*>", string.Empty);
            ReplaceCurrentCompletionPrefix(textArea, text);
        }

        private static void ReplaceCurrentCompletionPrefix(AvaloniaEdit.Editing.TextArea textArea, string text)
        {
            var document = textArea.Document;
            var caretOffset = textArea.Caret.Offset;
            var startOffset = caretOffset;

            while (startOffset > 0 && IsCompletionChar(document.GetCharAt(startOffset - 1)))
                startOffset--;

            document.Replace(startOffset, caretOffset - startOffset, text);
            textArea.Caret.Offset = startOffset + text.Length;
        }

        private static bool IsCompletionChar(char ch) =>
            char.IsLetterOrDigit(ch) || ch == '_';

        private static Control CreateContent(LspCompletion completion)
        {
            var kind = GetKindLabel(completion.Kind);

            var label = new TextBlock
            {
                Text = completion.Label,
                FontFamily = new FontFamily("Menlo,Consolas,Cascadia Mono,monospace"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            };

            var kindBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x34)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x50)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 1),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = kind,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xD0)),
                },
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };

            Grid.SetColumn(label, 0);
            Grid.SetColumn(kindBadge, 1);

            grid.Children.Add(label);
            grid.Children.Add(kindBadge);

            return grid;
        }

        private static string GetKindLabel(LspCompletionKind kind) =>
            kind switch
            {
                LspCompletionKind.Method => "method",
                LspCompletionKind.Function => "function",
                LspCompletionKind.Constructor => "ctor",
                LspCompletionKind.Field => "field",
                LspCompletionKind.Variable => "var",
                LspCompletionKind.Class => "class",
                LspCompletionKind.Interface => "interface",
                LspCompletionKind.Module => "module",
                LspCompletionKind.Property => "prop",
                LspCompletionKind.Enum => "enum",
                LspCompletionKind.EnumMember => "enum member",
                LspCompletionKind.Struct => "struct",
                LspCompletionKind.Event => "event",
                LspCompletionKind.Keyword => "keyword",
                LspCompletionKind.Snippet => "snippet",
                LspCompletionKind.Constant => "const",
                LspCompletionKind.TypeParameter => "typeparam",
                LspCompletionKind.Reference => "ref",
                LspCompletionKind.File => "file",
                LspCompletionKind.Folder => "folder",
                LspCompletionKind.Color => "color",
                LspCompletionKind.Operator => "operator",
                LspCompletionKind.Value => "value",
                LspCompletionKind.Unit => "unit",
                _ => "text",
            };
    }

    private sealed class BreakpointMargin : AbstractMargin
    {
        private IReadOnlyList<DebugBreakpoint> _breakpoints = [];
        private readonly Action<int> _toggleBreakpoint;
        private int _hoveredLine = -1;

        private static readonly IBrush GhostBrush = Brushes.DarkRed;

        public BreakpointMargin(Action<int> toggleBreakpoint)
        {
            _toggleBreakpoint = toggleBreakpoint;
            Width = 22;
            Cursor = new Cursor(StandardCursorType.Arrow);
        }

        public void Update(IReadOnlyList<DebugBreakpoint> breakpoints)
        {
            _breakpoints = breakpoints;
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (TextView is null || !TextView.VisualLinesValid) return;

            foreach (var vl in TextView.VisualLines)
            {
                var lineNumber = vl.FirstDocumentLine.LineNumber;
                var y = vl.VisualTop - TextView.ScrollOffset.Y + vl.Height / 2;
                var bp = _breakpoints.FirstOrDefault(b => b.Line == lineNumber);

                if (bp is not null)
                {
                    var brush = bp.IsVerified ? Brushes.IndianRed : Brushes.DarkRed;
                    context.DrawEllipse(brush, null, new Point(11, y), 6, 6);
                }
                else if (lineNumber == _hoveredLine)
                {
                    context.DrawEllipse(GhostBrush, null, new Point(11, y), 6, 6);
                }
            }
        }

        public void SetHoveredLine(int lineNumber)
        {
            if (_hoveredLine == lineNumber) return;
            _hoveredLine = lineNumber;
            InvalidateVisual();
            if (lineNumber == -1) return;
            var hasBreakpoint = _breakpoints.Any(b => b.Line == lineNumber);
            ToolTip.SetTip(this, hasBreakpoint ? "Click to remove breakpoint" : "Click to add a breakpoint");
        }

        public bool TryToggleAtY(double y, double scrollOffsetY, Func<int, bool> toggleBreakpoint)
        {
            if (TextView is null || !TextView.VisualLinesValid) return false;
            var vt = y + scrollOffsetY;
            var vl = TextView.VisualLines.FirstOrDefault(l =>
                vt >= l.VisualTop && vt <= l.VisualTop + l.Height);
            if (vl is null) return false;
            toggleBreakpoint(vl.FirstDocumentLine.LineNumber);
            return true;
        }
    }

    private sealed class DiagnosticRenderer : IBackgroundRenderer
    {
        private IReadOnlyList<LspDiagnostic> _diagnostics = [];
        private TextDocument? _document;

        public KnownLayer Layer => KnownLayer.Selection;

        public void Update(TextDocument document, IReadOnlyList<LspDiagnostic> diagnostics)
        {
            _document = document;
            _diagnostics = diagnostics;
        }

        public LspDiagnostic? FindDiagnosticAt(int line, int character) =>
            _diagnostics.FirstOrDefault(d =>
                d.StartLine == line &&
                character >= d.StartCharacter &&
                character <= d.EndCharacter);

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!textView.VisualLinesValid || _document is null || _diagnostics.Count == 0)
                return;

            foreach (var diag in _diagnostics)
            {
                var startLine = diag.StartLine + 1; // LSP 0-based → AvaloniaEdit 1-based
                if (startLine < 1 || startLine > _document.LineCount)
                    continue;

                var visualLine = textView.VisualLines.FirstOrDefault(vl =>
                    vl.FirstDocumentLine.LineNumber == startLine);
                if (visualLine is null)
                    continue;

                var color = diag.Severity == LspDiagnosticSeverity.Error
                    ? Color.FromRgb(255, 80, 80)
                    : Color.FromRgb(220, 180, 80);

                var pen = new Pen(new SolidColorBrush(color), 1.5, new DashStyle([2, 2], 0));

                // Compute x span from actual start/end character positions
                var startCol = diag.StartCharacter + 1;
                var endLine = diag.EndLine + 1;
                var endCol = endLine != startLine
                    ? _document.GetLineByNumber(startLine).Length + 1
                    : diag.EndCharacter + 1;

                var startPos = new TextViewPosition(startLine, startCol);
                var endPos   = new TextViewPosition(startLine, endCol);

                var x0 = textView.GetVisualPosition(startPos, VisualYPosition.LineBottom).X
                         - textView.ScrollOffset.X;
                var x1 = textView.GetVisualPosition(endPos, VisualYPosition.LineBottom).X
                         - textView.ScrollOffset.X;

                if (x1 <= x0) x1 = x0 + 4;
                x0 = Math.Max(x0, 0);
                x1 = Math.Min(x1, textView.Bounds.Width);
                if (x1 <= 0) continue;

                var y = visualLine.VisualTop + visualLine.Height - textView.ScrollOffset.Y - 1;
                drawingContext.DrawLine(pen, new Point(x0, y), new Point(x1, y));
            }
        }
    }

    private sealed class SemanticColorizer : DocumentColorizingTransformer
    {
        private SemanticToken[] _tokens = [];

        public void Update(SemanticToken[] tokens) => _tokens = tokens;

        protected override void ColorizeLine(DocumentLine line)
        {
            if (_tokens.Length == 0) return;

            var lineIndex = line.LineNumber - 1; // LSP 0-based
            var lineStart = line.Offset;
            var lineLength = line.Length;

            foreach (var token in _tokens)
            {
                if (token.Line != lineIndex) continue;
                if (token.StartChar >= lineLength) continue;

                var brush = TokenTypeToBrush(token.TokenType, token.Modifiers);
                if (brush is null) continue;

                var start = lineStart + token.StartChar;
                var end = Math.Min(start + token.Length, lineStart + lineLength);
                if (end <= start) continue;

                ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(brush));
            }
        }

        private static IBrush? TokenTypeToBrush(string tokenType, string[] modifiers)
        {
            // OmniSharp uses "staticSymbol" as a token TYPE (not modifier) for static members.
            // Standard LSP modifier "static" is also checked as fallback.
            var isStatic = tokenType == "staticSymbol" || Array.IndexOf(modifiers, "static") >= 0;

            return tokenType switch
            {
                // Types — OmniSharp names
                "class" or "delegateName" or "record" => SemanticBrushes.Type,
                "interface" => SemanticBrushes.Interface,
                "enum" => SemanticBrushes.Enum,
                "struct" or "recordStruct" => SemanticBrushes.Struct,
                "typeParameter" => SemanticBrushes.TypeParameter,
                "namespace" or "module" => null,

                // Members — OmniSharp names
                "method" or "extensionMethod" => SemanticBrushes.Method,
                "property" => SemanticBrushes.Property,
                "field" when isStatic => SemanticBrushes.ConstantField,
                "field" => SemanticBrushes.Field,
                "enumMember" => SemanticBrushes.EnumMember,
                "event" => SemanticBrushes.Method,

                // Locals — OmniSharp uses "local" for local variables
                "local" or "parameter" => SemanticBrushes.Variable,

                // Static catch-all: when OmniSharp emits staticSymbol as the type
                "staticSymbol" => SemanticBrushes.ConstantField,

                _ => null,
            };
        }

        private static class SemanticBrushes
        {
            public static readonly ISolidColorBrush Type         = new SolidColorBrush(Color.Parse("#4EC9B0"));
            public static readonly ISolidColorBrush Interface    = new SolidColorBrush(Color.Parse("#B8D7A3"));
            public static readonly ISolidColorBrush Struct       = new SolidColorBrush(Color.Parse("#86C691"));
            public static readonly ISolidColorBrush Enum         = new SolidColorBrush(Color.Parse("#B8D7A3"));
            public static readonly ISolidColorBrush EnumMember   = new SolidColorBrush(Color.Parse("#51B6C4"));
            public static readonly ISolidColorBrush TypeParameter = new SolidColorBrush(Color.Parse("#B8D7A3"));
            public static readonly ISolidColorBrush Method       = new SolidColorBrush(Color.Parse("#DCDCAA"));
            public static readonly ISolidColorBrush Property     = new SolidColorBrush(Color.Parse("#9CDCFE"));
            public static readonly ISolidColorBrush Field        = new SolidColorBrush(Color.Parse("#D4D4D4"));
            public static readonly ISolidColorBrush ConstantField = new SolidColorBrush(Color.Parse("#51B6C4"));
            public static readonly ISolidColorBrush Variable     = new SolidColorBrush(Color.Parse("#9CDCFE"));
        }
    }

    private sealed class DebugLineRenderer : IBackgroundRenderer
    {
        private DebugExecutionLine? _executionLine;

        public KnownLayer Layer => KnownLayer.Background;

        public void Update(DebugExecutionLine? executionLine)
        {
            _executionLine = executionLine;
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!textView.VisualLinesValid)
                return;

            foreach (var line in textView.VisualLines)
            {
                if (_executionLine?.Line == line.FirstDocumentLine.LineNumber)
                {
                    var rect = new Rect(0, line.VisualTop - textView.ScrollOffset.Y, textView.Bounds.Width, line.Height);
                    drawingContext.FillRectangle(new SolidColorBrush(Color.FromArgb(42, 122, 92, 255)), rect);
                }
            }
        }
    }
}
