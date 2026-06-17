using System;
using System.Collections.Generic;
using Fluence.Core.Models.Workspace.Enums;

namespace Fluence.Core.Models.Workspace;

public sealed class Workspace
{
    private readonly Dictionary<string, ModuleState> _moduleStates = new(StringComparer.Ordinal);

    public WorkspaceMode Mode { get; private set; } = WorkspaceMode.Empty;

    public string? CurrentFilePath { get; private set; }

    public string? CurrentFolderPath { get; private set; }

    public string? CurrentSolutionPath { get; private set; }

    public string? StartupProjectPath { get; private set; }

    public WorkspaceMode? ModeBeforeDebugging { get; private set; }

    public TabSession TabSession { get; private set; } = TabSession.Empty;

    public IReadOnlyDictionary<string, ModuleState> ModuleStates => _moduleStates;

    public void SetMode(WorkspaceMode mode)
    {
        if (mode == WorkspaceMode.Debugging && Mode != WorkspaceMode.Debugging)
            ModeBeforeDebugging = Mode;

        if (Mode == WorkspaceMode.Debugging && mode != WorkspaceMode.Debugging)
            ModeBeforeDebugging = null;

        Mode = mode;
    }

    public void OpenFile(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var document = OpenDocument.FromPath(path, content);
        if (Mode is WorkspaceMode.Empty or WorkspaceMode.FileOnly)
        {
            Mode = WorkspaceMode.FileOnly;
            CurrentFolderPath = null;
            CurrentSolutionPath = null;
            ModeBeforeDebugging = null;
        }

        TabSession = TabSession.AddOrActivate(document);
        CurrentFilePath = TabSession.ActiveDocument?.Path;
    }

    public void OpenToolTab(string id, string displayName, object contentViewModel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(contentViewModel);

        var document = OpenDocument.FromTool(id, displayName, contentViewModel);
        TabSession = TabSession.AddOrActivate(document);
        CurrentFilePath = TabSession.ActiveDocument?.Kind == OpenDocumentKind.TextDocument
            ? TabSession.ActiveDocument.Path
            : null;
    }

    public void UpdateActiveDocumentContent(string content)
    {
        if (TabSession.ActiveDocument?.Kind == OpenDocumentKind.TextDocument)
        {
            TabSession.ActiveDocument.UpdateContent(content);
        }
    }

    public void ReloadDocument(string path, string content)
    {
        var document = TabSession.Documents
            .FirstOrDefault(d => string.Equals(d.Path, path, StringComparison.OrdinalIgnoreCase));
        document?.ReloadContent(content);
    }

    public void MarkActiveDocumentSaved()
    {
        TabSession.ActiveDocument?.MarkSaved();
    }

    public void OpenFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Mode = WorkspaceMode.Folder;
        CurrentFilePath = null;
        CurrentFolderPath = path;
        CurrentSolutionPath = null;
        ModeBeforeDebugging = null;
        TabSession = TabSession.Empty;
    }

    public void OpenSolution(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Mode = WorkspaceMode.Solution;
        CurrentFilePath = null;
        CurrentFolderPath = null;
        CurrentSolutionPath = path;
        StartupProjectPath = null;
        ModeBeforeDebugging = null;
        TabSession = TabSession.Empty;
    }

    public void SetStartupProject(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        StartupProjectPath = path;
    }

    public void ActivateDocument(string path)
    {
        TabSession = TabSession.Activate(path);
        CurrentFilePath = TabSession.ActiveDocument?.Kind == OpenDocumentKind.TextDocument
            ? TabSession.ActiveDocument.Path
            : null;
    }

    public void CloseDocument(string path)
    {
        TabSession = TabSession.Close(path);
        CurrentFilePath = TabSession.ActiveDocument?.Kind == OpenDocumentKind.TextDocument
            ? TabSession.ActiveDocument.Path
            : null;

        if (Mode == WorkspaceMode.FileOnly && TabSession.ActiveDocument is null)
        {
            Reset();
        }
    }

    public void Reset()
    {
        Mode = WorkspaceMode.Empty;
        CurrentFilePath = null;
        CurrentFolderPath = null;
        CurrentSolutionPath = null;
        StartupProjectPath = null;
        TabSession = TabSession.Empty;
    }

    public void SetModuleState(string moduleName, ModuleState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        _moduleStates[moduleName] = state;
    }
}
