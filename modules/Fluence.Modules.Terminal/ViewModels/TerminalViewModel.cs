using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Infrastructure;
using Fluence.Core.ViewModels;
using Fluence.Core.Workspace;

namespace Fluence.Modules.Terminal.ViewModels;

public sealed partial class TerminalViewModel : ViewModelBase
{
    private readonly ITerminalService _terminalService;
    private readonly IWorkspaceContext _workspace;

    [ObservableProperty]
    private string _currentCommand = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public TerminalViewModel(ITerminalService terminalService, IWorkspaceContext workspace)
    {
        _terminalService = terminalService;
        _workspace = workspace;
        _terminalService.LineReceived += OnLineReceived;
        _terminalService.Cleared += OnCleared;
    }

    public ObservableCollection<TerminalLineViewModel> Lines { get; } = [];

    public bool HasLines => Lines.Count > 0;

    [RelayCommand]
    private async Task ExecuteCurrentCommandAsync()
    {
        var command = CurrentCommand;
        CurrentCommand = string.Empty;

        if (string.IsNullOrWhiteSpace(command))
            return;

        await ExecuteAsync(command);
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        await _terminalService.CancelAsync();
    }

    public async Task ExecuteAsync(string command)
    {
        SetBusy(true);

        try
        {
            await _terminalService.ExecuteAsync(command, GetWorkingDirectory());
        }
        finally
        {
            SetBusy(_terminalService.IsBusy);
        }
    }

    private void OnLineReceived(object? sender, TerminalLineEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Lines.Add(new TerminalLineViewModel(e.Line, e.IsError));
            OnPropertyChanged(nameof(HasLines));
            SetBusy(_terminalService.IsBusy);
        });
    }

    private void OnCleared(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Lines.Clear();
            OnPropertyChanged(nameof(HasLines));
        });
    }

    private void SetBusy(bool value)
    {
        if (IsBusy == value) return;
        IsBusy = value;
        StopCommand.NotifyCanExecuteChanged();
    }

    private string? GetWorkingDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_workspace.Current.CurrentFolderPath))
            return _workspace.Current.CurrentFolderPath;

        var solutionPath = _workspace.Current.CurrentSolutionPath;
        return string.IsNullOrWhiteSpace(solutionPath) ? null : Path.GetDirectoryName(solutionPath);
    }
}
