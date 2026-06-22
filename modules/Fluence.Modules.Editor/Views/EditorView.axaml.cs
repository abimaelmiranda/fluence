using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.TextMate;
using Fluence.Core.Models.Keybindings;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.LanguageServer;
using Fluence.Core.Models.Theming;
using Fluence.Core.Services;
using Fluence.Modules.Editor.Completion;
using Fluence.Modules.Editor.Rendering;
using Fluence.Modules.Editor.ViewModels;
using Fluence.Core.Events.Document;
using Fluence.Core.Events.Lsp;
using TextMateSharp.Grammars;

namespace Fluence.Modules.Editor.Views;

public partial class EditorView : UserControl
{
    // ── Debounce delays ────────────────────────────────────────────────────
    private static readonly TimeSpan TextSyncDebounceDelay   = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan CompletionDebounceDelay = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan DotCompletionDelay      = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan HoverDebounceDelay      = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan LspHoverDebounceDelay   = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan CompletionRefreshDelay  = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan PopupCloseDelay         = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan ViewStateSaveDelay      = TimeSpan.FromMilliseconds(250);

    // ── Language scope lookup ───────────────────────────────────────────────
    private static readonly Dictionary<string, string> LanguageScopeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"]      = "source.cs",
        [".csproj"]  = "text.xml",
        [".props"]   = "text.xml",
        [".targets"] = "text.xml",
        [".slnx"]    = "text.xml",
        [".xml"]     = "text.xml",
        [".axaml"]   = "text.xml",
        [".xaml"]    = "text.xml",
    };

    // ── Auto-pair tables ────────────────────────────────────────────────────
    private static readonly Dictionary<char, char> AutoPairClosers = new()
    {
        ['(']  = ')',
        ['[']  = ']',
        ['{']  = '}',
        ['"']  = '"',
        ['\''] = '\'',
    };
    private static readonly HashSet<char> AutoPairClosingChars = new(AutoPairClosers.Values);

    // ── Cached static brushes ───────────────────────────────────────────────
    private static readonly ISolidColorBrush SignatureGrayBrush    = new SolidColorBrush(Color.Parse("#AAAACC"));
    private static readonly ISolidColorBrush SignatureWhiteBrush   = new SolidColorBrush(Colors.White);
    private static readonly ISolidColorBrush DiagErrorForeground   = new SolidColorBrush(Color.FromRgb(255, 144, 144));
    private static readonly ISolidColorBrush DiagWarningForeground = new SolidColorBrush(Color.FromRgb(255, 208, 100));
    private static readonly ISolidColorBrush DiagErrorBorder       = new SolidColorBrush(Color.FromRgb(90, 32, 32));
    private static readonly ISolidColorBrush DiagWarningBorder     = new SolidColorBrush(Color.FromRgb(90, 74, 0));

    // ── Renderers & margin ──────────────────────────────────────────────────
    private readonly DebugLineRenderer    _debugLineRenderer    = new();
    private readonly DiagnosticRenderer   _diagnosticRenderer   = new();
    private readonly SemanticColorizer    _semanticColorizer    = new(); // TODO when adding theme customization, rework to support dynamic theme changes.
    private BreakpointMargin?             _breakpointMargin;

    // ── State flags ─────────────────────────────────────────────────────────
    private bool   _isUpdatingEditorText;
    private bool   _mouseInPopup;

    // ── Comment cache (IsInsideComment hot-path optimization) ───────────────
    private bool _cachedBlockCommentState;
    private int  _blockCommentCacheLineNumber = -1;

    // ── TextMate ────────────────────────────────────────────────────────────
    private RegistryOptions?         _registryOptions;
    private TextMate.Installation?   _textMateInstallation;

    // ── Services ────────────────────────────────────────────────────────────
    private EditorViewModel?         _viewModel;
    private EditorSettings           _editorSettings = new();
    private IDisposable?             _editorSettingsSubscription;
    private IDisposable?             _keybindingsSubscription;
    private IDisposable?             _themeSubscription;
    private ICompletionService?      _completionService;
    private IHoverService?           _hoverService;
    private ISignatureHelpService?   _signatureHelpService;
    private ICodeActionService?      _codeActionService;
    private IShellEventBus?          _eventBus;

    // ── Completion state ────────────────────────────────────────────────────
    private List<LspCompletionData>? _activeCompletions;
    private readonly object          _completionGate = new();
    private Timer?                   _completionTimer;
    private DispatcherTimer?         _completionRefreshTimer;
    private CompletionRequest?       _pendingCompletionRequest;
    private int                      _completionRequestVersion;
    private int                      _completionTriggerOffset = -1;
    private bool                     _completionRequestInFlight;
    private bool                     _completionRefreshPending;

    // ── Hover state ─────────────────────────────────────────────────────────
    private readonly object  _hoverGate = new();
    private readonly object  _lspHoverGate = new();
    private readonly object  _popupCloseGate = new();
    private Timer?           _hoverTimer;
    private Timer?           _lspHoverTimer;
    private Timer?           _popupCloseTimer;
    private HoverRequest?    _pendingHoverRequest;
    private LspHoverRequest? _pendingLspHoverRequest;
    private int              _hoverRequestVersion;
    private int              _lspHoverRequestVersion;
    private bool             _hoverRequestInFlight;
    private bool             _lspHoverRequestInFlight;

    // ── Signature help state ────────────────────────────────────────────────
    private int _signatureHelpVersion;
    private int _signatureTriggerOffset = -1;

    // ── Text sync debounce ──────────────────────────────────────────────────
    private Timer? _textSyncTimer;
    private Timer? _viewStateSaveTimer;

    // ── Semantic redraw throttle ────────────────────────────────────────────
    private bool              _semanticRedrawPending;
    private SemanticToken[]?  _pendingSemanticTokens;
    private string?           _pendingSemanticTokensPath;
    private int               _pendingSemanticTokensVersion;
    private readonly Dictionary<string, CachedSemanticTokens> _semanticTokensByPath = new(StringComparer.OrdinalIgnoreCase);

    // ── Document path tracking ──────────────────────────────────────────────
    private string? _lastKnownDocumentPath;
    private string? _dismissedExceptionPopupKey;
    private string? _savedViewStateDuringTextSwitchPath;
    private string? _pendingViewStateSavePath;
    private ScrollViewer? _editorScrollViewer;
    private bool    _isSwitchingDocumentViewState;
    private bool    _isEditorHiddenForDocumentSwitch;
    private double  _editorOpacityBeforeDocumentSwitch = 1;
    private int     _viewStateRestoreVersion;
    private int     _explicitNavigationVersion;
    private int     _viewStateTransitionVersion;
    private int     _pendingViewStateSaveTransitionVersion;

    // ── Undo acceleration ──────────────────────────────────────────────────
    private DateTime _lastUndoShortcutAt = DateTime.MinValue;
    private int      _undoShortcutRepeatCount;

    // ── Request record types ────────────────────────────────────────────────
    private sealed record CompletionRequest(string FilePath, int Line, int Character, int CaretOffset, int Version);
    private sealed record HoverRequest(string FilePath, string Expression, Point HoverPoint, int Version);
    private sealed record LspHoverRequest(string FilePath, int Line, int Character, Point HoverPoint, int Version);
    private sealed record CachedSemanticTokens(int Version, SemanticToken[] Tokens);

    public EditorView()
    {
        InitializeComponent();
        DataContextChanged    += OnDataContextChanged;
        AttachedToVisualTree  += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        Editor.TextChanged    += OnEditorTextChanged;
        Editor.LostFocus      += OnEditorLostFocus;
        _breakpointMargin = new BreakpointMargin(line => _viewModel?.ToggleBreakpoint(line));
        Editor.TextArea.LeftMargins.Insert(0, _breakpointMargin);
        EnsureEditorScrollViewerSubscription();
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
        Editor.TextArea.TextView.PointerHover        += OnPointerHover;
        Editor.TextArea.TextView.PointerHoverStopped += OnPointerHoverStopped;
        HoverPopupBorder.PointerEntered    += (_, _) => { _mouseInPopup = true;  CancelPopupClose(); };
        HoverPopupBorder.PointerExited     += (_, _) => { _mouseInPopup = false; ClosePopupDelayed(); };
        LspHoverBorder.PointerEntered      += (_, _) => { _mouseInPopup = true;  CancelPopupClose(); };
        LspHoverBorder.PointerExited       += (_, _) => { _mouseInPopup = false; ClosePopupDelayed(); };
        CodeActionPopupBorder.PointerEntered += (_, _) => { _mouseInPopup = true;  CancelPopupClose(); };
        CodeActionPopupBorder.PointerExited  += (_, _) => { _mouseInPopup = false; ClosePopupDelayed(); };
        DebugExceptionCloseButton.Click += (_, _) => DismissDebugExceptionPopup();
        Editor.TextArea.TextEntering    += OnTextEntering;
        Editor.TextArea.TextEntered     += OnTextEntered;
        Editor.AddHandler(KeyDownEvent, OnEditorPreviewKeyDown, RoutingStrategies.Tunnel, true);
        Editor.AddHandler(KeyUpEvent, OnEditorPreviewKeyUp, RoutingStrategies.Tunnel, true);
        EnsureEditorTimers();
        _completionRefreshTimer = new DispatcherTimer { Interval = CompletionRefreshDelay };
        _completionRefreshTimer.Tick += OnCompletionRefreshTimerTick;
        CompletionListBox.ItemTemplate = new FuncDataTemplate<LspCompletionData>(
            (data, _) => data is null ? new TextBlock() : (Control)data.Content,
            supportsRecycling: false);
        CodeActionsListBox.ItemTemplate = new FuncDataTemplate<CodeActionListItem>(
            (data, _) => data is null
                ? new TextBlock()
                : CreateCodeActionRow(data),
            supportsRecycling: false);
        CodeActionsListBox.DoubleTapped += OnCodeActionListBoxDoubleTapped;
        InitializeTextMate();
    }

    private Control CreateCodeActionRow(CodeActionListItem item)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            Cursor = new Cursor(StandardCursorType.Hand),
            Margin = new Thickness(0),
            MinHeight = 28,
        };

        var badge = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#25384C")),
            BorderBrush = new SolidColorBrush(Color.Parse("#3A5C7A")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1),
            Margin = new Thickness(4, 4, 8, 4),
            Child = new TextBlock
            {
                Text = item.Action.IsPreferred ? "FIX" : "ACTION",
                Foreground = new SolidColorBrush(Color.Parse("#8DBDF8")),
                FontFamily = new FontFamily("Menlo,Consolas,Cascadia Mono,monospace"),
                FontSize = 10,
            },
        };

        var label = new TextBlock
        {
            Text = item.Label,
            Foreground = new SolidColorBrush(Color.Parse("#D7E2EE")),
            FontFamily = new FontFamily("Menlo,Consolas,Cascadia Mono,monospace"),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };

        Grid.SetColumn(badge, 0);
        Grid.SetColumn(label, 1);
        grid.Children.Add(badge);
        grid.Children.Add(label);
        grid.PointerReleased += (_, _) =>
        {
            CodeActionsListBox.SelectedItem = item;
            ApplySelectedCodeAction();
        };
        return grid;
    }

    private TimeSpan CompletionDebounce => TimeSpan.FromMilliseconds(
        Math.Clamp(_editorSettings.CompletionTriggerDelayMs, 20, 2_000));

    private void ApplyEditorSettings(EditorSettings settings)
    {
        _editorSettings = settings;
        Dispatcher.UIThread.Post(() =>
        {
            var fontFamily = new FontFamily(settings.FontFamily);
            Editor.FontFamily = fontFamily;
            Editor.FontSize = settings.FontSize;
            Editor.Options.LineHeightFactor = Math.Clamp(settings.LineHeightFactor, 1.0, 1.8);
            Editor.ShowLineNumbers = settings.ShowLineNumbers;
            Editor.TextArea.TextView.Redraw();
            Editor.TextArea.TextView.InvalidateMeasure();
            Editor.InvalidateVisual();
            UpdateMenuGestures();
        });
    }

    private void UpdateMenuGestures()
    {
        if (_viewModel is null)
            return;

        static KeyGesture? TryParse(string? gesture)
        {
            if (string.IsNullOrWhiteSpace(gesture))
                return null;

            try
            {
                return KeyGesture.Parse(gesture);
            }
            catch
            {
                return null;
            }
        }

        KeyGesture? ParseGesture(string commandId) => TryParse(_viewModel.Keybindings.GetGesture(commandId));

        MenuItemIntelliSense.InputGesture = ParseGesture(CommandIds.EditorTriggerCompletion);
        MenuItemDuplicateLine.InputGesture = ParseGesture(CommandIds.EditorDuplicateLine);
        MenuItemGoToDefinition.InputGesture = ParseGesture(CommandIds.EditorGoToDefinition);
        MenuItemGoToImplementation.InputGesture = ParseGesture(CommandIds.EditorGoToImplementation);
        MenuItemGoToTypeDefinition.InputGesture = ParseGesture(CommandIds.EditorGoToTypeDefinition);
    }

    private void ApplyTheme(IdeTheme theme)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Editor.Background = new SolidColorBrush(Color.Parse(theme.Colors.EditorBackground));
            Editor.Foreground = new SolidColorBrush(Color.Parse(theme.Colors.EditorForeground));
            _semanticColorizer.ApplyTheme(theme.SemanticTokenColors);
            ApplyTextMateThemeJson(theme.TextMateThemeJson);
            Editor.TextArea.TextView.Redraw();
            Editor.InvalidateVisual();
        });
    }

    public void SetServices(
        ICompletionService completionService,
        IShellEventBus eventBus,
        IHoverService? hoverService = null,
        ISignatureHelpService? signatureHelpService = null,
        ICodeActionService? codeActionService = null)
    {
        if (_eventBus is not null)
        {
            _eventBus.UnsubscribeSync<DocumentClosedEvent>(OnDocumentClosed);
            _eventBus.UnsubscribeSync<DiagnosticsUpdatedEvent>(OnDiagnosticsUpdated);
            _eventBus.UnsubscribeSync<NavigationResolvedEvent>(OnNavigationResolved);
            _eventBus.UnsubscribeSync<SemanticTokensUpdatedEvent>(OnSemanticTokensUpdated);
            _eventBus.UnsubscribeSync<WorkspaceEditRequestedEvent>(OnWorkspaceEditRequested);
        }

        _completionService    = completionService;
        _eventBus             = eventBus;
        _hoverService         = hoverService;
        _signatureHelpService = signatureHelpService;
        _codeActionService    = codeActionService;

        eventBus.SubscribeSync<DocumentClosedEvent>(OnDocumentClosed);
        eventBus.SubscribeSync<DiagnosticsUpdatedEvent>(OnDiagnosticsUpdated);
        eventBus.SubscribeSync<NavigationResolvedEvent>(OnNavigationResolved);
        eventBus.SubscribeSync<SemanticTokensUpdatedEvent>(OnSemanticTokensUpdated);
        eventBus.SubscribeSync<WorkspaceEditRequestedEvent>(OnWorkspaceEditRequested);
    }

    private void OnWorkspaceEditRequested(WorkspaceEditRequestedEvent e)
    {
        if (!string.Equals(_viewModel?.ActiveDocumentPath, e.FilePath, StringComparison.OrdinalIgnoreCase))
            return;

        if (Dispatcher.UIThread.CheckAccess())
            ApplyWorkspaceEdits(e.Edits);
        else
            Dispatcher.UIThread.Post(() => ApplyWorkspaceEdits(e.Edits));
    }

    private void ApplyWorkspaceEdits(IReadOnlyList<LspTextEdit> edits)
    {
        var doc = Editor.Document;
        if (doc is null) return;

        var sorted = System.Linq.Enumerable.OrderByDescending(edits, e => (e.StartLine, e.StartCharacter)).ToArray();

        doc.BeginUpdate();
        try
        {
            foreach (var edit in sorted)
            {
                var startLineNum = Math.Clamp(edit.StartLine + 1, 1, doc.LineCount);
                var endLineNum   = Math.Clamp(edit.EndLine   + 1, 1, doc.LineCount);
                var startLine    = doc.GetLineByNumber(startLineNum);
                var endLine      = doc.GetLineByNumber(endLineNum);
                var startOffset  = Math.Min(startLine.Offset + edit.StartCharacter, startLine.EndOffset);
                var endOffset    = Math.Min(endLine.Offset   + edit.EndCharacter,   endLine.EndOffset);
                startOffset = Math.Max(0, Math.Min(startOffset, doc.TextLength));
                endOffset   = Math.Max(startOffset, Math.Min(endOffset, doc.TextLength));
                doc.Replace(startOffset, endOffset - startOffset, edit.NewText);
            }
        }
        finally
        {
            doc.EndUpdate();
        }
    }
}
