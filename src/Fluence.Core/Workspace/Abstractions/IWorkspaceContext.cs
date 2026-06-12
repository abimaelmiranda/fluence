using System;

namespace Fluence.Core.Workspace;

public interface IWorkspaceContext
{
    Workspace Current { get; }

    event EventHandler? Changed;

    void SetMode(WorkspaceMode mode);

    void OpenFile(string path, string content);

    void OpenFolder(string path);

    void OpenSolution(string path);

    void SetStartupProject(string path);

    void ActivateDocument(string path);

    void CloseDocument(string path);

    void UpdateActiveDocumentContent(string content);

    void MarkActiveDocumentSaved();

    void Reset();

    void SetModuleState(string moduleName, ModuleState state);
}
