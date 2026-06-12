using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluence.Application.Workspace;
using Fluence.Core.Commands;
using Fluence.Core.Workspace;

namespace Fluence.Desktop.ViewModels;

public sealed partial class EditorViewModel : ViewModelBase
{
    private readonly IWorkspaceContext _workspace;
    private readonly ICommandHandler<SaveActiveDocumentCommand> _saveHandler;
    private bool _isRefreshingFromWorkspace;

    [ObservableProperty]
    private string _activeText = string.Empty;

    public EditorViewModel(IWorkspaceContext workspace, ICommandHandler<SaveActiveDocumentCommand> saveHandler)
    {
        _workspace = workspace;
        _saveHandler = saveHandler;
        RefreshFromWorkspace();
        _workspace.Changed += OnWorkspaceChanged;
    }

    public bool HasActiveDocument => _workspace.Current.TabSession.ActiveDocument is not null;

    public string? ActiveDocumentPath => _workspace.Current.TabSession.ActiveDocument?.Path;

    public async Task SaveIfDirtyAsync()
    {
        if (_workspace.Current.TabSession.ActiveDocument?.IsDirty == true)
        {
            await _saveHandler.HandleAsync(new SaveActiveDocumentCommand());
        }
    }

    partial void OnActiveTextChanged(string value)
    {
        if (_isRefreshingFromWorkspace)
        {
            return;
        }

        _workspace.UpdateActiveDocumentContent(value);
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        RefreshFromWorkspace();
    }

    private void RefreshFromWorkspace()
    {
        _isRefreshingFromWorkspace = true;
        ActiveText = _workspace.Current.TabSession.ActiveDocument?.Content ?? string.Empty;
        _isRefreshingFromWorkspace = false;

        OnPropertyChanged(nameof(HasActiveDocument));
        OnPropertyChanged(nameof(ActiveDocumentPath));
    }
}
