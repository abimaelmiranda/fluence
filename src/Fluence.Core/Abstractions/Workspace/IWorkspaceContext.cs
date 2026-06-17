using System;
using Fluence.Core.Models.Workspace.Enums;
using WorkspaceEntity = Fluence.Core.Models.Workspace.Workspace;

namespace Fluence.Core.Abstractions.Workspace;

public interface IWorkspaceContext
{
    WorkspaceEntity Current { get; }

    event EventHandler? Changed;

    void SetMode(WorkspaceMode mode);

    void OpenFile(string path, string content);

    void OpenToolTab(string id, string displayName, object contentViewModel);

    void OpenFolder(string path);

    void OpenSolution(string path);

    void SetStartupProject(string path);

    void ActivateDocument(string path);

    void CloseDocument(string path);

    void UpdateActiveDocumentContent(string content);

    void ReloadDocument(string path, string content);

    void MarkActiveDocumentSaved();

    void Reset();

    void SetModuleState(string moduleName, ModuleState state);
}
