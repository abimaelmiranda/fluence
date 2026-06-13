using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Infrastructure;
using Fluence.Core.ViewModels;
using Fluence.Core.Workspace;
using XTerminal = global::XTerm.Terminal;

namespace Fluence.Modules.Terminal.ViewModels;

public sealed partial class TerminalViewModel : ViewModelBase, IDisposable
{
    private readonly ITerminalService _terminalService;
    private readonly IWorkspaceContext _workspace;
    private TerminalSessionViewModel? _activeSession;

    public TerminalViewModel(ITerminalService terminalService, IWorkspaceContext workspace)
    {
        _terminalService = terminalService;
        _workspace = workspace;
        _terminalService.SessionsChanged += OnSessionsChanged;

        SyncSessions();
    }

    public ObservableCollection<TerminalSessionViewModel> Sessions { get; } = [];

    public TerminalSessionViewModel? ActiveSession
    {
        get => _activeSession;
        private set
        {
            if (SetProperty(ref _activeSession, value))
            {
                OnPropertyChanged(nameof(HasActiveSession));
                OnPropertyChanged(nameof(CanCloseTerminal));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(ActiveTerminal));
            }
        }
    }

    public bool HasActiveSession => ActiveSession is not null;

    public bool CanCloseTerminal => ActiveSession is not null;

    public bool IsEmpty => ActiveSession is null;

    public XTerminal? ActiveTerminal => ActiveSession?.XTerminal;

    public bool CanCreateTerminal => _terminalService.CanCreateSession;

    public int MaxSessions => _terminalService.MaxSessions;

    [RelayCommand(CanExecute = nameof(CanCreateTerminal))]
    private void CreateTerminal()
    {
        if (!_terminalService.CanCreateSession)
            return;

        _terminalService.CreateSession();
    }

    [RelayCommand(CanExecute = nameof(CanCloseTerminal))]
    private async Task CloseTerminalAsync()
    {
        var session = ActiveSession?.Session;
        if (session is null)
            return;

        try
        {
            await _terminalService.CloseSessionAsync(session);
        }
        catch
        {
            // Terminal close is intentionally fail-safe; teardown continues best-effort in the service.
        }

        SyncSessions();
    }

    public async Task ExecuteAsync(string command)
    {
        if (ActiveSession is null)
            _terminalService.CreateSession();

        if (ActiveSession is not null)
            await ActiveSession.ExecuteAsync(command);
    }

    private void ActivateSession(TerminalSessionViewModel session)
    {
        _terminalService.SetActiveSession(session.Session);
    }

    private void OnSessionsChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(SyncSessions);
    }

    private void SyncSessions()
    {
        var serviceSessions = _terminalService.Sessions;

        // Remove ViewModels for sessions that no longer exist. Do this BEFORE adding
        // new ones so the collection is clean when we resolve the active session below.
        foreach (var viewModel in Sessions.ToArray())
        {
            if (!serviceSessions.Contains(viewModel.Session))
            {
                Sessions.Remove(viewModel);
                viewModel.Dispose();
            }
        }

        // Add ViewModels for sessions that don't have one yet.
        foreach (var session in serviceSessions)
        {
            if (Sessions.Any(viewModel => ReferenceEquals(viewModel.Session, session)))
                continue;

            Sessions.Add(new TerminalSessionViewModel(session, _workspace, ActivateSession));
        }

        for (var i = 0; i < Sessions.Count; i++)
            Sessions[i].Title = $"Terminal {i + 1}";

        // Resolve active session only after all VMs exist. Setting ActiveSession before
        // the new VM is added would produce a null assignment that clears the terminal.
        var active = Sessions.FirstOrDefault(viewModel => ReferenceEquals(viewModel.Session, _terminalService.ActiveSession));
        foreach (var session in Sessions)
            session.IsActive = ReferenceEquals(session, active);

        ActiveSession = active;
        OnPropertyChanged(nameof(CanCreateTerminal));
        OnPropertyChanged(nameof(CanCloseTerminal));
        OnPropertyChanged(nameof(MaxSessions));
        CreateTerminalCommand.NotifyCanExecuteChanged();
        CloseTerminalCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        _terminalService.SessionsChanged -= OnSessionsChanged;

        foreach (var session in Sessions)
            session.Dispose();

        Sessions.Clear();
    }
}
