using System;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Workspace.Enums;
using WorkspaceEntity = Fluence.Core.Models.Workspace.Workspace;

namespace Fluence.Core.Services.Workspace;

public sealed class WorkspaceContext : IWorkspaceContext
{
    public WorkspaceEntity Current { get; } = new();

    public event EventHandler? Changed;

    public void SetMode(WorkspaceMode mode)
    {
        Current.SetMode(mode);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void OpenFile(string path, string content)
    {
        Current.OpenFile(path, content);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void OpenToolTab(string id, string displayName, object contentViewModel)
    {
        Current.OpenToolTab(id, displayName, contentViewModel);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void OpenFolder(string path)
    {
        Current.OpenFolder(path);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void OpenSolution(string path)
    {
        Current.OpenSolution(path);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetStartupProject(string path)
    {
        Current.SetStartupProject(path);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ActivateDocument(string path)
    {
        Current.ActivateDocument(path);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void CloseDocument(string path)
    {
        Current.CloseDocument(path);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateActiveDocumentContent(string content)
    {
        Current.UpdateActiveDocumentContent(content);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ReloadDocument(string path, string content)
    {
        Current.ReloadDocument(path, content);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkActiveDocumentSaved()
    {
        Current.MarkActiveDocumentSaved();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        Current.Reset();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetModuleState(string moduleName, ModuleState state)
    {
        Current.SetModuleState(moduleName, state);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
