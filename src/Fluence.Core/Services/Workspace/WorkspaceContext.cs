using System;
using Fluence.Core.Abstractions.Tasks;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using WorkspaceEntity = Fluence.Core.Models.Workspace.Workspace;

namespace Fluence.Core.Services.Workspace;

public sealed class WorkspaceContext : IWorkspaceContext
{
    private readonly IUiDispatcher? _dispatcher;

    public WorkspaceContext() { }

    public WorkspaceContext(IUiDispatcher dispatcher) { _dispatcher = dispatcher; }

    public WorkspaceEntity Current { get; } = new();

    public event EventHandler? Changed;

    private void FireChanged()
    {
        if (_dispatcher is not null)
            _dispatcher.Post(() => Changed?.Invoke(this, EventArgs.Empty));
        else
            Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetMode(WorkspaceMode mode)
    {
        Current.SetMode(mode);
        FireChanged();
    }

    public void OpenFile(string path, string content)
    {
        Current.OpenFile(path, content);
        FireChanged();
    }

    public void OpenToolTab(string id, string displayName, object contentViewModel)
    {
        Current.OpenToolTab(id, displayName, contentViewModel);
        FireChanged();
    }

    public void OpenFolder(string path)
    {
        Current.OpenFolder(path);
        FireChanged();
    }

    public void OpenSolution(string path)
    {
        Current.OpenSolution(path);
        FireChanged();
    }

    public void SetStartupProject(string path)
    {
        Current.SetStartupProject(path);
        FireChanged();
    }

    public void ActivateDocument(string path)
    {
        Current.ActivateDocument(path);
        FireChanged();
    }

    public void CloseDocument(string path)
    {
        Current.CloseDocument(path);
        FireChanged();
    }

    public void UpdateActiveDocumentContent(string content)
    {
        Current.UpdateActiveDocumentContent(content);
        FireChanged();
    }

    public void ReloadDocument(string path, string content)
    {
        Current.ReloadDocument(path, content);
        FireChanged();
    }

    public void MarkActiveDocumentSaved()
    {
        Current.MarkActiveDocumentSaved();
        FireChanged();
    }

    public void Reset()
    {
        Current.Reset();
        FireChanged();
    }

    public void SetModuleState(string moduleName, ModuleState state)
    {
        Current.SetModuleState(moduleName, state);
        FireChanged();
    }
}
