using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.TextMate;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.LanguageServer;
using Fluence.Modules.Editor.Completion;
using Fluence.Modules.Editor.Rendering;
using Fluence.Modules.Editor.ViewModels;
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
    private ICompletionService?      _completionService;
    private IHoverService?           _hoverService;
    private ISignatureHelpService?   _signatureHelpService;
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

    // ── Semantic redraw throttle ────────────────────────────────────────────
    private bool              _semanticRedrawPending;
    private SemanticToken[]?  _pendingSemanticTokens;

    // ── Document path tracking ──────────────────────────────────────────────
    private string? _lastKnownDocumentPath;

    // ── Undo acceleration ──────────────────────────────────────────────────
    private DateTime _lastUndoShortcutAt = DateTime.MinValue;
    private int      _undoShortcutRepeatCount;

    // ── Request record types ────────────────────────────────────────────────
    private sealed record CompletionRequest(string FilePath, int Line, int Character, int CaretOffset, int Version);
    private sealed record HoverRequest(string FilePath, string Expression, Point HoverPoint, int Version);
    private sealed record LspHoverRequest(string FilePath, int Line, int Character, Point HoverPoint, int Version);

    public EditorView()
    {
        InitializeComponent();
        DataContextChanged    += OnDataContextChanged;
        AttachedToVisualTree  += OnAttachedToVisualTree;
        Editor.TextChanged    += OnEditorTextChanged;
        Editor.LostFocus      += OnEditorLostFocus;
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
        Editor.TextArea.TextView.PointerHover        += OnPointerHover;
        Editor.TextArea.TextView.PointerHoverStopped += OnPointerHoverStopped;
        HoverPopupBorder.PointerEntered += (_, _) => { _mouseInPopup = true;  CancelPopupClose(); };
        HoverPopupBorder.PointerExited  += (_, _) => { _mouseInPopup = false; ClosePopupDelayed(); };
        LspHoverBorder.PointerEntered   += (_, _) => { _mouseInPopup = true;  CancelPopupClose(); };
        LspHoverBorder.PointerExited    += (_, _) => { _mouseInPopup = false; ClosePopupDelayed(); };
        Editor.TextArea.TextEntering    += OnTextEntering;
        Editor.TextArea.TextEntered     += OnTextEntered;
        Editor.AddHandler(KeyDownEvent, OnEditorPreviewKeyDown, RoutingStrategies.Tunnel, true);
        _textSyncTimer = new Timer(
            static state => ((EditorView)state!).OnTextSyncTimerElapsed(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
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
        _completionService      = completionService;
        _eventBus               = eventBus;
        _hoverService           = hoverService;
        _signatureHelpService   = signatureHelpService;

        eventBus.SubscribeSync<DiagnosticsUpdatedEvent>(OnDiagnosticsUpdated);
        eventBus.SubscribeSync<NavigationResolvedEvent>(OnNavigationResolved);
        eventBus.SubscribeSync<SemanticTokensUpdatedEvent>(OnSemanticTokensUpdated);
        eventBus.SubscribeSync<LspServerReadyEvent>(OnLspServerReady);
    }
}
