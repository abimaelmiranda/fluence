using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluence.Core.Abstractions.Commands;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Keybindings;
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
using Fluence.Core.Services.Modules;
using Fluence.Core.ViewModels;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services.Workspace;
using Fluence.Modules.Editor.Commands;

namespace Fluence.Modules.Editor.ViewModels;

public sealed partial class EditorViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(750);

    private readonly IWorkspaceContext _workspace;
    private readonly ICommandHandler<SaveActiveDocumentCommand> _saveHandler;
    private readonly IDebugStateService _debugState;
    private readonly IDebugService _debugService;
    private readonly IShellEventBus _events;
    private readonly ITaskScheduler _scheduler;
    private readonly ISettingsService _settings;
    private readonly IKeybindingService _keybindings;
    private readonly ICommandRegistry _commands;
    private readonly ICompletionService? _completionService;
    private readonly IHoverService? _hoverService;
    private readonly ISignatureHelpService? _signatureHelpService;
    private readonly ICodeActionService? _codeActionService;
    private readonly IFormattingService? _formattingService;
    private readonly IThemeLoader? _themeLoader;
    private readonly DispatcherTimer _autoSaveTimer;
    private readonly DispatcherTimer _formatFeedbackTimer;
    private bool _isRefreshingFromWorkspace;
    private string? _pendingAutoSavePath;
    private string? _lastOpenedDocumentPath;
    private string? _lastLiveSyncedPath;
    private string? _lastLiveSyncedText;
    private int _documentVersion;

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
        ITaskScheduler scheduler,
        ISettingsService settings,
        IKeybindingService keybindings,
        ICommandRegistry commands,
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
        _scheduler = scheduler;
        _settings = settings;
        _keybindings = keybindings;
        _commands = commands;
        _completionService = completionService;
        _hoverService = hoverService;
        _signatureHelpService = signatureHelpService;
        _codeActionService = codeActionService;
        _formattingService = formattingService;
        _themeLoader = themeLoader;
        _autoSaveTimer = new DispatcherTimer { Interval = AutoSaveDelay };
        _autoSaveTimer.Tick += OnAutoSaveTimerTick;
        _formatFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1_200) };
        _formatFeedbackTimer.Tick += OnFormatFeedbackTimerTick;
        RegisterEditorCommands();
        RefreshFromWorkspace();
        _workspace.Changed += OnWorkspaceChanged;
        _debugState.Changed += OnDebugStateChanged;
    }

    public ICompletionService? CompletionService => _completionService;
    public IHoverService? HoverService => _hoverService;
    public ISignatureHelpService? SignatureHelpService => _signatureHelpService;
    public ICodeActionService? CodeActionService => _codeActionService;
    public IThemeLoader? ThemeLoader => _themeLoader;
    public IShellEventBus EventBus => _events;
    public ITaskScheduler TaskScheduler => _scheduler;
    public ISettingsService Settings => _settings;
    public IKeybindingService Keybindings => _keybindings;

    public bool HasActiveDocument => _workspace.Current.TabSession.ActiveDocument?.Kind == OpenDocumentKind.TextDocument;

    public string? ActiveDocumentPath => _workspace.Current.TabSession.ActiveDocument?.Kind == OpenDocumentKind.TextDocument
        ? _workspace.Current.TabSession.ActiveDocument.Path
        : null;

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

    private void RegisterEditorCommands()
    {
        _commands.Register(new IdeCommandDefinition(
            CommandIds.EditorTriggerCompletion,
            "Trigger Completion",
            KeybindingScope.Editor,
            "Ctrl+Space"));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.EditorQuickFix,
            "Quick Fix",
            KeybindingScope.Editor,
            OperatingSystem.IsMacOS() ? "Meta+." : "Ctrl+."));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.EditorGoToDefinition,
            "Go to Definition",
            KeybindingScope.Editor,
            "F12"));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.EditorGoToImplementation,
            "Go to Implementation",
            KeybindingScope.Editor,
            "Ctrl+F12"));
        _commands.Register(new IdeCommandDefinition(
            CommandIds.EditorGoToTypeDefinition,
            "Go to Type Definition",
            KeybindingScope.Editor,
            "Ctrl+Shift+F12"));
    }

    public Task<DebugVariable?> EvaluateHoverAsync(string expression, CancellationToken cancellationToken) =>
        _debugService.EvaluateAsync(expression, cancellationToken);

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
            var filePath = ActiveDocumentPath;
            if (filePath is null) return;

            var uriKey = new Uri(filePath).AbsoluteUri;
            if (!resolved.Edit.Changes.TryGetValue(uriKey, out var edits)) return;

            var sorted = edits.OrderByDescending(e => (e.StartLine, e.StartCharacter)).ToArray();
            var text = ActiveText;
            foreach (var edit in sorted)
            {
                var start = GetTextOffset(text, edit.StartLine, edit.StartCharacter);
                var end   = GetTextOffset(text, edit.EndLine, edit.EndCharacter);
                if (start < 0 || end < start || end > text.Length) continue;
                text = string.Concat(text.AsSpan(0, start), edit.NewText, text.AsSpan(end));
            }
            ActiveText = text;
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

    public void ToggleBreakpoint(int line)
    {
        if (string.IsNullOrWhiteSpace(ActiveDocumentPath) || line <= 0)
            return;

        _events.Publish(new ToggleBreakpointRequestedEvent(ActiveDocumentPath, line));
    }

    public void PublishGoToDefinition(int line, int character)
    {
        if (string.IsNullOrWhiteSpace(ActiveDocumentPath)) return;
        _events.Publish(new GoToDefinitionRequestedEvent(ActiveDocumentPath, line, character));
    }

    public void PublishGoToImplementation(int line, int character)
    {
        if (string.IsNullOrWhiteSpace(ActiveDocumentPath)) return;
        _events.Publish(new GoToImplementationRequestedEvent(ActiveDocumentPath, line, character));
    }

    public void PublishGoToTypeDefinition(int line, int character)
    {
        if (string.IsNullOrWhiteSpace(ActiveDocumentPath)) return;
        _events.Publish(new GoToTypeDefinitionRequestedEvent(ActiveDocumentPath, line, character));
    }

    public void PublishLiveDocumentChanged(string content, bool flushImmediately)
    {
        var path = ActiveDocumentPath;
        if (path is null || !IsTextDocument(path))
            return;

        _lastLiveSyncedPath = path;
        _lastLiveSyncedText = content;
        _events.Publish(new DocumentLiveChangedEvent(
            path,
            content,
            Interlocked.Increment(ref _documentVersion),
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

        if (path is not null && IsTextDocument(path))
        {
            if (string.Equals(path, _lastLiveSyncedPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(value, _lastLiveSyncedText, StringComparison.Ordinal))
            {
                return;
            }

            _events.Publish(new DocumentChangedEvent(path, value, Interlocked.Increment(ref _documentVersion)));
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
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshDebugStateProperties();
            return;
        }

        Dispatcher.UIThread.Post(RefreshDebugStateProperties);
    }

    private void RefreshDebugStateProperties()
    {
        OnPropertyChanged(nameof(ActiveDocumentBreakpoints));
        OnPropertyChanged(nameof(ActiveExecutionLine));
        OnPropertyChanged(nameof(ActiveExceptionStop));
    }

    private void RefreshFromWorkspace()
    {
        var previousPath = _lastOpenedDocumentPath;

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

        var newPath = ActiveDocumentPath;

        // Notify LSP when switching away from a document
        if (previousPath is not null && !string.Equals(previousPath, newPath, StringComparison.OrdinalIgnoreCase))
            _events.Publish(new DocumentClosedEvent(previousPath));

        // Notify LSP when a new text document is opened
        if (newPath is not null && IsTextDocument(newPath) &&
            !string.Equals(newPath, previousPath, StringComparison.OrdinalIgnoreCase))
        {
            _lastOpenedDocumentPath = newPath;
            _events.Publish(new DocumentOpenedEvent(newPath, ActiveText, "csharp"));
        }
        else if (newPath is null)
        {
            _lastOpenedDocumentPath = null;
        }
    }

    private static bool IsTextDocument(string path) =>
        System.IO.Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase);

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
        if (!Dispatcher.UIThread.CheckAccess())
            return RunOnUiThreadAsync(PrepareManualFormatOnUiThread, cancellationToken);

        return Task.FromResult(PrepareManualFormatOnUiThread());
    }

    private ManualFormatRequest? PrepareManualFormatOnUiThread()
    {
        CancelPendingAutoSave();

        var activeDocument = _workspace.Current.TabSession.ActiveDocument;
        if (activeDocument is not { Kind: OpenDocumentKind.TextDocument } ||
            !IsTextDocument(activeDocument.Path) ||
            !_settings.Get<EditorSettings>().FormatOnSave)
        {
            return null;
        }

        _events.Publish(new LspInteractiveRequestStartedEvent(activeDocument.Path));
        ShowFormatFeedback("Formatting...", autoHide: false);
        if (_formattingService is null)
        {
            ShowFormatFeedback("Format unavailable", autoHide: true);
            return null;
        }

        var content = ActiveText;
        var syncVersion = Interlocked.Increment(ref _documentVersion);
        _lastLiveSyncedPath = activeDocument.Path;
        _lastLiveSyncedText = content;
        return new ManualFormatRequest(activeDocument.Path, content, syncVersion);
    }

    private Task CompleteManualFormatAsync(
        ManualFormatRequest request,
        IReadOnlyList<LspTextEdit> edits,
        CancellationToken cancellationToken)
    {
        if (!Dispatcher.UIThread.CheckAccess())
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
            ShowFormatFeedback("Format skipped", autoHide: true);
            return;
        }

        if (edits.Count == 0)
        {
            ShowFormatFeedback("Already formatted", autoHide: true);
            return;
        }

        var formatted = ApplyTextEdits(ActiveText, edits);
        if (string.Equals(formatted, ActiveText, StringComparison.Ordinal))
        {
            ShowFormatFeedback("Already formatted", autoHide: true);
            return;
        }

        ActiveText = formatted;
        PublishLiveDocumentChanged(formatted, flushImmediately: true);
        ShowFormatFeedback("Formatted", autoHide: true);
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

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    private static Task<T> RunOnUiThreadAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<T>(cancellationToken);

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(() =>
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

    private static Task RunOnUiThreadAsync(Action action, CancellationToken cancellationToken) =>
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
    }
}
