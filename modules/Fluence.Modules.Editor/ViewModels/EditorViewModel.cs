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
using Fluence.Core.Abstractions.LanguageServer;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Models.LanguageServer;
using Fluence.Core.Services.Debugging;
using Fluence.Core.Abstractions.Modules;
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
    private readonly ICompletionService? _completionService;
    private readonly IHoverService? _hoverService;
    private readonly ISignatureHelpService? _signatureHelpService;
    private readonly ICodeActionService? _codeActionService;
    private readonly DispatcherTimer _autoSaveTimer;
    private bool _isRefreshingFromWorkspace;
    private string? _pendingAutoSavePath;
    private string? _lastOpenedDocumentPath;
    private int _documentVersion;

    [ObservableProperty]
    private string _activeText = string.Empty;

    public EditorViewModel(
        IWorkspaceContext workspace,
        ICommandHandler<SaveActiveDocumentCommand> saveHandler,
        IDebugStateService debugState,
        IDebugService debugService,
        IShellEventBus events,
        ICompletionService? completionService = null,
        IHoverService? hoverService = null,
        ISignatureHelpService? signatureHelpService = null,
        ICodeActionService? codeActionService = null)
    {
        _workspace = workspace;
        _saveHandler = saveHandler;
        _debugState = debugState;
        _debugService = debugService;
        _events = events;
        _completionService = completionService;
        _hoverService = hoverService;
        _signatureHelpService = signatureHelpService;
        _codeActionService = codeActionService;
        _autoSaveTimer = new DispatcherTimer { Interval = AutoSaveDelay };
        _autoSaveTimer.Tick += OnAutoSaveTimerTick;
        RefreshFromWorkspace();
        _workspace.Changed += OnWorkspaceChanged;
        _debugState.Changed += OnDebugStateChanged;
    }

    public ICompletionService? CompletionService => _completionService;
    public IHoverService? HoverService => _hoverService;
    public ISignatureHelpService? SignatureHelpService => _signatureHelpService;
    public ICodeActionService? CodeActionService => _codeActionService;
    public IShellEventBus EventBus => _events;

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

    public bool IsDebuggerStopped => _debugState.Snapshot.IsStopped;

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

    public async Task SaveIfDirtyAsync()
    {
        await SaveIfDirtyAsync(expectedPath: null, CancellationToken.None);
    }

    partial void OnActiveTextChanged(string value)
    {
        if (_isRefreshingFromWorkspace) return;
        _workspace.UpdateActiveDocumentContent(value);
        ScheduleAutoSave(ActiveDocumentPath);

        var path = ActiveDocumentPath;
        if (path is not null && IsTextDocument(path))
            _events.Publish(new DocumentChangedEvent(path, value, Interlocked.Increment(ref _documentVersion)));
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        CancelPendingAutoSave();
        RefreshFromWorkspace();
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

    private void CancelPendingAutoSave()
    {
        _pendingAutoSavePath = null;
        _autoSaveTimer.Stop();
    }

    public void Dispose()
    {
        CancelPendingAutoSave();
        _autoSaveTimer.Tick -= OnAutoSaveTimerTick;
        _workspace.Changed -= OnWorkspaceChanged;
        _debugState.Changed -= OnDebugStateChanged;
    }
}
