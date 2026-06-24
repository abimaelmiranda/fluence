using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Languages;
using Fluence.Core.Abstractions.Keybindings;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Abstractions.Theming;
using Fluence.Core.Models.Keybindings;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Models.LanguageServer;
using Fluence.Core.Services.Debugging;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Models.Modules;
using Fluence.Core.Models.Modules.Enums;
using Fluence.Core.ViewModels;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.Editor.Commands;
using Fluence.Modules.Editor.Services;
using Fluence.Core.Events.Debug;
using Fluence.Core.Events.Document;
using Fluence.Core.Events.Lsp;
using Fluence.Core.Events.Provisioning;
using Fluence.Core.Events.Workspace;
using Fluence.Core.Requests.Lsp;

namespace Fluence.Modules.Editor.ViewModels;

public sealed partial class EditorViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(1_000);

    private readonly IWorkspaceContext _workspace;
    private readonly ICommandHandler<SaveActiveDocumentCommand> _saveHandler;
    private readonly IDebugStateService _debugState;
    private readonly IDebugService _debugService;
    private readonly IShellEventBus _events;
    private readonly IShellRequestBus _requests;
    private readonly ITaskScheduler _scheduler;
    private readonly IUiDispatcher _dispatcher;
    private readonly ISettingsService _settings;
    private readonly ILanguageProfileRegistry _languageProfiles;
    private readonly EditorViewStateStore _viewStateStore;
    private readonly IKeybindingService _keybindings;
    private readonly ICommandRegistry _commands;
    private readonly ICompletionService? _completionService;
    private readonly IHoverService? _hoverService;
    private readonly ISignatureHelpService? _signatureHelpService;
    private readonly ICodeActionService? _codeActionService;
    private readonly IFormattingService? _formattingService;
    private readonly IThemeLoader? _themeLoader;
    private readonly ILocalizationService _loc;
    private readonly DispatcherTimer _autoSaveTimer;
    private readonly DispatcherTimer _formatFeedbackTimer;
    private bool _isRefreshingFromWorkspace;
    private string? _pendingAutoSavePath;
    private string? _lastLiveSyncedPath;
    private string? _lastLiveSyncedText;
    private int _documentVersion;
    private readonly Dictionary<string, int> _documentVersions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _openTextDocumentPaths = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _activeText = string.Empty;

    [ObservableProperty]
    private bool _isFormatOnSaveFeedbackVisible;

    [ObservableProperty]
    private string _formatOnSaveFeedbackText = string.Empty;

    private sealed record ManualFormatRequest(string FilePath, string Content, int Version);

    public EditorViewModel(
        IWorkspaceContext workspace,
        ICommandHandler<SaveActiveDocumentCommand> saveHandler,
        IDebugStateService debugState,
        IDebugService debugService,
        IShellEventBus events,
        IShellRequestBus requests,
        ITaskScheduler scheduler,
        IUiDispatcher dispatcher,
        ISettingsService settings,
        ILanguageProfileRegistry languageProfiles,
        EditorViewStateStore viewStateStore,
        IKeybindingService keybindings,
        ICommandRegistry commands,
        ILocalizationService localization,
        ICompletionService? completionService = null,
        IHoverService? hoverService = null,
        ISignatureHelpService? signatureHelpService = null,
        ICodeActionService? codeActionService = null,
        IFormattingService? formattingService = null,
        IThemeLoader? themeLoader = null)
    {
        _workspace = workspace;
        _saveHandler = saveHandler;
        _debugState = debugState;
        _debugService = debugService;
        _events = events;
        _requests = requests;
        _scheduler = scheduler;
        _dispatcher = dispatcher;
        _settings = settings;
        _languageProfiles = languageProfiles;
        _viewStateStore = viewStateStore;
        _keybindings = keybindings;
        _commands = commands;
        _completionService = completionService;
        _hoverService = hoverService;
        _signatureHelpService = signatureHelpService;
        _codeActionService = codeActionService;
        _formattingService = formattingService;
        _themeLoader = themeLoader;
        _loc = localization;
        _autoSaveTimer = new DispatcherTimer { Interval = AutoSaveDelay };
        _autoSaveTimer.Tick += OnAutoSaveTimerTick;
        _formatFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1_200) };
        _formatFeedbackTimer.Tick += OnFormatFeedbackTimerTick;
        RegisterEditorCommands();
        RefreshFromWorkspace();
        _workspace.Changed += OnWorkspaceChanged;
        _debugState.Changed += OnDebugStateChanged;
        _events.SubscribeSync<LspServerReadyEvent>(OnLspServerReady);
    }

    public ICompletionService? CompletionService => _completionService;
    public IHoverService? HoverService => _hoverService;
    public ISignatureHelpService? SignatureHelpService => _signatureHelpService;
    public ICodeActionService? CodeActionService => _codeActionService;
    public IThemeLoader? ThemeLoader => _themeLoader;
    public ILocalizationService Localization => _loc;
    public IShellEventBus EventBus => _events;
    public ITaskScheduler TaskScheduler => _scheduler;
    public IUiDispatcher Dispatcher => _dispatcher;
    public ISettingsService Settings => _settings;
    public IKeybindingService Keybindings => _keybindings;

    public bool HasActiveDocument => _workspace.Current.TabSession.ActiveDocument?.Kind == OpenDocumentKind.TextDocument;

    public string? ActiveDocumentPath => _workspace.Current.TabSession.ActiveDocument?.Kind == OpenDocumentKind.TextDocument
        ? _workspace.Current.TabSession.ActiveDocument.Path
        : null;

    public int ActiveDocumentVersion =>
        ActiveDocumentPath is { } path && _documentVersions.TryGetValue(path, out var version)
            ? version
            : 0;

    public IReadOnlyList<DebugBreakpoint> ActiveDocumentBreakpoints =>
        string.IsNullOrWhiteSpace(ActiveDocumentPath)
            ? Array.Empty<DebugBreakpoint>()
            : _debugState.Snapshot.Breakpoints
                .Where(b => string.Equals(b.FilePath, ActiveDocumentPath, StringComparison.OrdinalIgnoreCase))
                .ToArray();

    public DebugExecutionLine? ActiveExecutionLine
    {
        get
        {
            var currentLine = _debugState.Snapshot.CurrentLine;
            return currentLine is not null &&
                   string.Equals(currentLine.FilePath, ActiveDocumentPath, StringComparison.OrdinalIgnoreCase)
                ? currentLine
                : null;
        }
    }

    public DebugExceptionInfo? ActiveExceptionStop
    {
        get
        {
            var snapshot = _debugState.Snapshot;
            return snapshot.ExceptionInfo is not null &&
                   snapshot.CurrentLine is not null &&
                   string.Equals(snapshot.CurrentLine.FilePath, ActiveDocumentPath, StringComparison.OrdinalIgnoreCase)
                ? snapshot.ExceptionInfo
                : null;
        }
    }

    public bool IsDebuggerStopped => _debugState.Snapshot.IsStopped;

    public string? ActiveDocumentLanguageId =>
        ActiveDocumentPath is null
            ? null
            : _languageProfiles.DetectLanguageForFile(_workspace, ActiveDocumentPath);

    private void RegisterEditorCommands()
    {
        _commands.Register(new IdeCommandDefinition(
            CommandIds.EditorTriggerCompletion,
            _loc.Get("Editor.Command.TriggerCompletion"),
            KeybindingScope.Editor,
            "Ctrl+Space"));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.EditorQuickFix,
            _loc.Get("Editor.Command.QuickFix"),
            KeybindingScope.Editor,
            OperatingSystem.IsMacOS() ? "Meta+." : "Ctrl+."));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.EditorDuplicateLine,
            _loc.Get("Editor.Command.DuplicateLine"),
            KeybindingScope.Editor,
            OperatingSystem.IsMacOS() ? "Meta+D" : "Ctrl+D"));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.EditorGoToDefinition,
            _loc.Get("Editor.Command.GoToDefinition"),
            KeybindingScope.Editor,
            "F12"));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.EditorGoToImplementation,
            _loc.Get("Editor.Command.GoToImplementation"),
            KeybindingScope.Editor,
            "Ctrl+F12"));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.EditorGoToTypeDefinition,
            _loc.Get("Editor.Command.GoToTypeDefinition"),
            KeybindingScope.Editor,
            "Ctrl+Shift+F12"));
    }

    public Task<DebugVariable?> EvaluateHoverAsync(string expression, CancellationToken cancellationToken) =>
        _debugService.EvaluateAsync(expression, cancellationToken);

    public Task<EditorDocumentViewState?> GetViewStateAsync(string filePath, CancellationToken cancellationToken = default) =>
        _viewStateStore.GetAsync(filePath, cancellationToken);

    public Task SaveViewStateAsync(EditorDocumentViewState viewState, CancellationToken cancellationToken = default) =>
        _viewStateStore.SaveAsync(viewState, cancellationToken);

    public async Task ApplyFixAllAsync(
        LspCodeAction templateAction,
        LspDiagnostic[] diagnostics,
        CancellationToken cancellationToken = default)
    {
        if (_codeActionService is null || string.IsNullOrWhiteSpace(ActiveDocumentPath))
            return;

        var filePath = ActiveDocumentPath;
        var allEdits = new Dictionary<string, List<LspTextEdit>>(StringComparer.OrdinalIgnoreCase);

        foreach (var diagnostic in diagnostics)
        {
            if (cancellationToken.IsCancellationRequested) return;

            var actions = await _codeActionService.GetCodeActionsAsync(
                filePath,
                diagnostic.StartLine, diagnostic.StartCharacter,
                diagnostic.EndLine, diagnostic.EndCharacter,
                diagnostic, cancellationToken).ConfigureAwait(false);

            var match = actions.FirstOrDefault(a =>
                string.Equals(a.Title, templateAction.Title, StringComparison.OrdinalIgnoreCase));
            if (match is null) continue;

            var resolved = match;
            if (match.Edit is null && match.CommandIdentifier is null && match.RawJson is not null)
                resolved = await _codeActionService.ResolveAsync(match, cancellationToken) ?? match;
            if (resolved.Edit is null) continue;

            foreach (var (uri, edits) in resolved.Edit.Changes)
            {
                if (!allEdits.TryGetValue(uri, out var list))
                    allEdits[uri] = list = [];
                list.AddRange(edits);
            }
        }

        if (cancellationToken.IsCancellationRequested) return;

        foreach (var (uri, edits) in allEdits)
        {
            var targetPath = new Uri(uri).LocalPath;
            var distinct = edits
                .GroupBy(e => (e.StartLine, e.StartCharacter, e.EndLine, e.EndCharacter, e.NewText))
                .Select(g => g.First())
                .ToArray();
            _events.Publish(new WorkspaceEditRequestedEvent(targetPath, distinct));
        }
    }

    public async Task ApplyCodeActionAsync(LspCodeAction action, CancellationToken cancellationToken = default)
    {
        System.Diagnostics.Debug.WriteLine($"[EditorVM] ApplyCodeAction: '{action.Title}' | service={_codeActionService is not null} | cmd={action.CommandIdentifier} | edit={action.Edit is not null} | hasRaw={action.RawJson is not null}");
        if (_codeActionService is null) return;

        // Lazy code action — resolve first to get edit or command
        var resolved = action;
        if (action.Edit is null && action.CommandIdentifier is null && action.RawJson is not null)
        {
            resolved = await _codeActionService.ResolveAsync(action, cancellationToken) ?? action;
            System.Diagnostics.Debug.WriteLine($"[EditorVM] Resolved: cmd={resolved.CommandIdentifier} | edit={resolved.Edit is not null}");
        }

        if (resolved.Edit is not null)
        {
            foreach (var (uri, edits) in resolved.Edit.Changes)
            {
                var filePath = new Uri(uri).LocalPath;
                _events.Publish(new WorkspaceEditRequestedEvent(filePath, edits));
            }
        }
        else if (resolved.CommandIdentifier is not null)
        {
            await _codeActionService.ExecuteCommandAsync(
                resolved.CommandIdentifier, resolved.CommandArgumentsJson, cancellationToken);
        }
    }

    private static int GetTextOffset(string text, int line, int character)
    {
        var currentLine = 0;
        var i = 0;
        while (i < text.Length && currentLine < line)
        {
            if (text[i] == '\n') currentLine++;
            i++;
        }
        return Math.Min(i + character, text.Length);
    }

    public Task<IReadOnlyList<DebugVariable>> GetChildVariablesAsync(int variablesReference, CancellationToken cancellationToken) =>
        _debugService.GetChildVariablesAsync(variablesReference, cancellationToken);

    public HoverVariableNode CreateHoverNode(DebugVariable variable) =>
        new(variable, GetChildVariablesAsync, _dispatcher);

    public void ToggleBreakpoint(int line)
    {
        if (string.IsNullOrWhiteSpace(ActiveDocumentPath) || line <= 0)
            return;

        _events.Publish(new ToggleBreakpointRequestedEvent(ActiveDocumentPath, line));
    }

    public void PublishGoToDefinition(int line, int character)
    {
        var path = ActiveDocumentPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        _scheduler.Schedule("editor.goto.definition", TaskPriority.Interactive,
            ct => SendNavigationRequestAsync(new GoToDefinitionRequest(path, line, character), ct));
    }

    public void PublishGoToImplementation(int line, int character)
    {
        var path = ActiveDocumentPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        _scheduler.Schedule("editor.goto.implementation", TaskPriority.Interactive,
            ct => SendNavigationRequestAsync(new GoToImplementationRequest(path, line, character), ct));
    }

    public void PublishGoToTypeDefinition(int line, int character)
    {
        var path = ActiveDocumentPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        _scheduler.Schedule("editor.goto.typedefinition", TaskPriority.Interactive,
            ct => SendNavigationRequestAsync(new GoToTypeDefinitionRequest(path, line, character), ct));
    }

    private async Task SendNavigationRequestAsync<TRequest>(TRequest request, CancellationToken ct)
        where TRequest : IShellRequest<LspLocation>
    {
        _events.Publish(new LspInteractiveRequestStartedEvent(ActiveDocumentPath ?? string.Empty));
        var location = await _requests.SendAsync<TRequest, LspLocation>(request, ct).ConfigureAwait(false);
        if (location is null || ct.IsCancellationRequested) return;

        if (!string.Equals(location.FilePath, ActiveDocumentPath, StringComparison.OrdinalIgnoreCase))
            _events.Publish(new OpenFileRequestedEvent(location.FilePath));

        _events.Publish(new NavigationResolvedEvent(location.FilePath, location.Line, location.Character));
    }

    public void PublishLiveDocumentChanged(string content, bool flushImmediately)
    {
        var path = ActiveDocumentPath;
        if (path is null || !IsTrackedTextDocument(path))
            return;

        _lastLiveSyncedPath = path;
        _lastLiveSyncedText = content;
        var version = NextDocumentVersion(path);
        _events.Publish(new DocumentLiveChangedEvent(
            path,
            content,
            version,
            flushImmediately));
    }

    public async Task SaveIfDirtyAsync()
    {
        await SaveIfDirtyAsync(expectedPath: null, CancellationToken.None);
    }

    public async Task SaveManuallyAsync(CancellationToken cancellationToken = default)
    {
        var formatRequest = await PrepareManualFormatAsync(cancellationToken).ConfigureAwait(false);
        if (formatRequest is not null && _formattingService is not null)
        {
            var edits = await _formattingService.FormatDocumentAsync(
                formatRequest.FilePath,
                formatRequest.Content,
                formatRequest.Version,
                cancellationToken).ConfigureAwait(false);

            await CompleteManualFormatAsync(formatRequest, edits, cancellationToken).ConfigureAwait(false);
        }

        await _saveHandler.HandleAsync(new SaveActiveDocumentCommand(), cancellationToken).ConfigureAwait(false);
    }

    partial void OnActiveTextChanged(string value)
    {
        if (_isRefreshingFromWorkspace) return;
        var path = ActiveDocumentPath;
        ScheduleAutoSave(path);
        _workspace.UpdateActiveDocumentContent(value);

        if (path is not null && IsTrackedTextDocument(path))
        {
            if (string.Equals(path, _lastLiveSyncedPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(value, _lastLiveSyncedText, StringComparison.Ordinal))
            {
                return;
            }

            var version = NextDocumentVersion(path);
            _events.Publish(new DocumentChangedEvent(path, value, version));
        }
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        if (!IsActiveDocumentContentRefresh())
        {
            CancelPendingAutoSave();
        }
        RefreshFromWorkspace();
    }

    private bool IsActiveDocumentContentRefresh()
    {
        var document = _workspace.Current.TabSession.ActiveDocument;
        return document is { Kind: OpenDocumentKind.TextDocument, IsDirty: true } &&
               !string.IsNullOrWhiteSpace(_pendingAutoSavePath) &&
               string.Equals(document.Path, _pendingAutoSavePath, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(document.Content, ActiveText, StringComparison.Ordinal);
    }

    private void OnDebugStateChanged(object? sender, EventArgs e)
    {
        if (_dispatcher.CheckAccess())
        {
            RefreshDebugStateProperties();
            return;
        }

        _dispatcher.Post(RefreshDebugStateProperties);
    }

    private void RefreshDebugStateProperties()
    {
        OnPropertyChanged(nameof(ActiveDocumentBreakpoints));
        OnPropertyChanged(nameof(ActiveExecutionLine));
        OnPropertyChanged(nameof(ActiveExceptionStop));
    }

    private void RefreshFromWorkspace()
    {
        _isRefreshingFromWorkspace = true;
        var activeDoc = _workspace.Current.TabSession.ActiveDocument;
        ActiveText = activeDoc?.Kind == OpenDocumentKind.TextDocument
            ? activeDoc.Content
            : string.Empty;
        _isRefreshingFromWorkspace = false;
        OnPropertyChanged(nameof(HasActiveDocument));
        OnPropertyChanged(nameof(ActiveDocumentPath));
        OnPropertyChanged(nameof(ActiveDocumentBreakpoints));
        OnPropertyChanged(nameof(ActiveExecutionLine));
        OnPropertyChanged(nameof(ActiveExceptionStop));

        SyncOpenTextDocuments();
        OnPropertyChanged(nameof(ActiveDocumentVersion));
    }

    private void SyncOpenTextDocuments()
    {
        var openDocuments = _workspace.Current.TabSession.Documents
            .Where(document => document.Kind == OpenDocumentKind.TextDocument && IsTrackedTextDocument(document.Path))
            .ToArray();
        var currentPaths = openDocuments
            .Select(document => document.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var path in _openTextDocumentPaths.Where(path => !currentPaths.Contains(path)).ToArray())
        {
            _openTextDocumentPaths.Remove(path);
            _documentVersions.Remove(path);
            CleanupDocumentWork(path);
            _events.Publish(new DocumentClosedEvent(path));
        }

        foreach (var document in openDocuments)
        {
            if (_openTextDocumentPaths.Contains(document.Path))
                continue;

            var languageId = ResolveDocumentLanguageId(document.Path);
            if (languageId is null)
                continue;

            _openTextDocumentPaths.Add(document.Path);
            var version = EnsureDocumentVersion(document.Path);
            _events.Publish(new DocumentOpenedEvent(document.Path, document.Content, languageId, version));
        }
    }

    private void OnLspServerReady(LspServerReadyEvent e)
    {
        RepublishOpenTextDocuments();
    }

    private void RepublishOpenTextDocuments()
    {
        foreach (var document in _workspace.Current.TabSession.Documents)
        {
            if (document.Kind != OpenDocumentKind.TextDocument || !IsTrackedTextDocument(document.Path))
                continue;

            var languageId = ResolveDocumentLanguageId(document.Path);
            if (languageId is null)
                continue;

            _openTextDocumentPaths.Add(document.Path);
            var version = EnsureDocumentVersion(document.Path);
            _events.Publish(new DocumentOpenedEvent(document.Path, document.Content, languageId, version));
        }
    }

    private int EnsureDocumentVersion(string path)
    {
        if (_documentVersions.TryGetValue(path, out var version))
            return version;

        version = Math.Max(1, Interlocked.Increment(ref _documentVersion));
        _documentVersions[path] = version;
        return version;
    }

    private int NextDocumentVersion(string path)
    {
        var version = Math.Max(1, Interlocked.Increment(ref _documentVersion));
        _documentVersions[path] = version;
        OnPropertyChanged(nameof(ActiveDocumentVersion));
        return version;
    }

    private void CleanupDocumentWork(string path)
    {
        _scheduler.CancelAndForget($"editor.hover.{path}");
        _scheduler.CancelAndForget($"editor.completion.{path}");
        _scheduler.CancelAndForget($"editor.code-actions.{path}");
        _scheduler.CancelAndForget($"editor.quick-fix.{path}");
        _scheduler.CancelAndForget($"editor.signature.{path}");
        _scheduler.CancelAndForget($"editor.view-state.save.{path}");
    }

    private bool IsTrackedTextDocument(string path) =>
        ResolveDocumentLanguageId(path) is not null;

    private string? ResolveDocumentLanguageId(string path) =>
        _languageProfiles.DetectLanguageForFile(_workspace, path);

    private void ScheduleAutoSave(string? documentPath)
    {
        if (string.IsNullOrWhiteSpace(documentPath))
            return;

        CancelPendingAutoSave();
        _pendingAutoSavePath = documentPath;
        _autoSaveTimer.Start();
    }

    private void OnAutoSaveTimerTick(object? sender, EventArgs e)
    {
        _autoSaveTimer.Stop();

        var documentPath = _pendingAutoSavePath;
        _pendingAutoSavePath = null;

        if (!string.IsNullOrWhiteSpace(documentPath))
        {
            _ = SaveAfterDebounceAsync(documentPath);
        }
    }

    private async Task SaveAfterDebounceAsync(string expectedPath)
    {
        try
        {
            await SaveIfDirtyAsync(expectedPath, CancellationToken.None);
        }
        catch
        {
        }
    }

    private async Task SaveIfDirtyAsync(string? expectedPath, CancellationToken cancellationToken)
    {
        var activeDocument = _workspace.Current.TabSession.ActiveDocument;
        if (activeDocument is not { Kind: OpenDocumentKind.TextDocument, IsDirty: true })
            return;

        if (!string.IsNullOrWhiteSpace(expectedPath) &&
            !string.Equals(activeDocument.Path, expectedPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await _saveHandler.HandleAsync(new SaveActiveDocumentCommand(), cancellationToken);
    }

    private Task<ManualFormatRequest?> PrepareManualFormatAsync(CancellationToken cancellationToken)
    {
        if (!_dispatcher.CheckAccess())
            return RunOnUiThreadAsync(PrepareManualFormatOnUiThread, cancellationToken);

        return Task.FromResult(PrepareManualFormatOnUiThread());
    }

    private ManualFormatRequest? PrepareManualFormatOnUiThread()
    {
        CancelPendingAutoSave();

        var activeDocument = _workspace.Current.TabSession.ActiveDocument;
        if (activeDocument is not { Kind: OpenDocumentKind.TextDocument } ||
            !IsTrackedTextDocument(activeDocument.Path) ||
            !_settings.Get<EditorSettings>().FormatOnSave)
        {
            return null;
        }

        _events.Publish(new LspInteractiveRequestStartedEvent(activeDocument.Path));
        ShowFormatFeedback(_loc.Get("Editor.Format.Feedback.Formatting"), autoHide: false);
        if (_formattingService is null)
        {
            ShowFormatFeedback(_loc.Get("Editor.Format.Feedback.Unavailable"), autoHide: true);
            return null;
        }

        var content = ActiveText;
        var syncVersion = NextDocumentVersion(activeDocument.Path);
        _lastLiveSyncedPath = activeDocument.Path;
        _lastLiveSyncedText = content;
        return new ManualFormatRequest(activeDocument.Path, content, syncVersion);
    }

    private Task CompleteManualFormatAsync(
        ManualFormatRequest request,
        IReadOnlyList<LspTextEdit> edits,
        CancellationToken cancellationToken)
    {
        if (!_dispatcher.CheckAccess())
            return RunOnUiThreadAsync(() => CompleteManualFormatOnUiThread(request, edits), cancellationToken);

        CompleteManualFormatOnUiThread(request, edits);
        return Task.CompletedTask;
    }

    private void CompleteManualFormatOnUiThread(ManualFormatRequest request, IReadOnlyList<LspTextEdit> edits)
    {
        if (!string.Equals(ActiveDocumentPath, request.FilePath, StringComparison.OrdinalIgnoreCase))
            return;

        if (!string.Equals(ActiveText, request.Content, StringComparison.Ordinal))
        {
            ShowFormatFeedback(_loc.Get("Editor.Format.Feedback.Skipped"), autoHide: true);
            return;
        }

        if (edits.Count == 0)
        {
            ShowFormatFeedback(_loc.Get("Editor.Format.Feedback.AlreadyFormatted"), autoHide: true);
            return;
        }

        var formatted = ApplyTextEdits(ActiveText, edits);
        if (string.Equals(formatted, ActiveText, StringComparison.Ordinal))
        {
            ShowFormatFeedback(_loc.Get("Editor.Format.Feedback.AlreadyFormatted"), autoHide: true);
            return;
        }

        ActiveText = formatted;
        PublishLiveDocumentChanged(formatted, flushImmediately: true);
        ShowFormatFeedback(_loc.Get("Editor.Format.Feedback.Formatted"), autoHide: true);
    }

    private static string ApplyTextEdits(string text, IReadOnlyList<LspTextEdit> edits)
    {
        foreach (var edit in edits.OrderByDescending(e => (e.StartLine, e.StartCharacter)))
        {
            var start = GetTextOffset(text, edit.StartLine, edit.StartCharacter);
            var end = GetTextOffset(text, edit.EndLine, edit.EndCharacter);
            if (start < 0 || end < start || end > text.Length)
                continue;

            text = string.Concat(text.AsSpan(0, start), edit.NewText, text.AsSpan(end));
        }

        return text;
    }

    private void CancelPendingAutoSave()
    {
        _pendingAutoSavePath = null;
        _autoSaveTimer.Stop();
    }

    private void ShowFormatFeedback(string text, bool autoHide)
    {
        void Apply()
        {
            _formatFeedbackTimer.Stop();
            FormatOnSaveFeedbackText = text;
            IsFormatOnSaveFeedbackVisible = true;
            if (autoHide)
                _formatFeedbackTimer.Start();
        }

        if (_dispatcher.CheckAccess())
            Apply();
        else
            _dispatcher.Post(Apply);
    }

    private Task<T> RunOnUiThreadAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<T>(cancellationToken);

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.Post(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                completion.TrySetResult(action());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });

        return completion.Task;
    }

    private Task RunOnUiThreadAsync(Action action, CancellationToken cancellationToken) =>
        RunOnUiThreadAsync(() =>
        {
            action();
            return true;
        }, cancellationToken);

    private void OnFormatFeedbackTimerTick(object? sender, EventArgs e)
    {
        _formatFeedbackTimer.Stop();
        IsFormatOnSaveFeedbackVisible = false;
    }

    public void Dispose()
    {
        CancelPendingAutoSave();
        _autoSaveTimer.Tick -= OnAutoSaveTimerTick;
        _formatFeedbackTimer.Stop();
        _formatFeedbackTimer.Tick -= OnFormatFeedbackTimerTick;
        _workspace.Changed -= OnWorkspaceChanged;
        _debugState.Changed -= OnDebugStateChanged;
        _events.UnsubscribeSync<LspServerReadyEvent>(OnLspServerReady);
    }
}
