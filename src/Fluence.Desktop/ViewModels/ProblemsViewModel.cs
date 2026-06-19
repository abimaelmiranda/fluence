using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Modules;
using Fluence.Core.Abstractions.Problems;
using Fluence.Core.Abstractions.Settings;
using Fluence.Core.Abstractions.Workspace;
using Fluence.Core.Models.Settings;
using Fluence.Core.Models.Workspace.Enums;
using Fluence.Core.Services;
using Fluence.Core.ViewModels;
using Fluence.Core.Events.Workspace;

namespace Fluence.Desktop.ViewModels;

public sealed partial class ProblemsViewModel : ViewModelBase, IDisposable
{
    private readonly IProblemService _problems;
    private readonly IShellEventBus _events;
    private readonly IWorkspaceContext _workspace;
    private readonly IDisposable _settingsSubscription;
    private ProblemsSettings _settings;
    private string? _lastActivePath;
    private int _refreshPending;

    public ProblemsViewModel(
        IProblemService problems,
        IShellEventBus events,
        IWorkspaceContext workspace,
        ISettingsService settings)
    {
        _problems = problems;
        _events = events;
        _workspace = workspace;
        _settings = settings.Get<ProblemsSettings>();
        _settingsSubscription = settings.Watch<ProblemsSettings>()
            .Subscribe(new ActionObserver<ProblemsSettings>(OnSettingsChanged));
        _problems.Changed += OnProblemsChanged;
        _workspace.Changed += OnWorkspaceChanged;
        Refresh();
    }

    public ObservableCollection<ProblemItemViewModel> Items { get; } = [];

    public bool IsEmpty => Items.Count == 0;

    [RelayCommand]
    private void Navigate(ProblemItemViewModel? item)
    {
        if (item is null)
            return;

        var problem = item.Problem;
        _events.Publish(new OpenFileAtLocationRequestedEvent(problem.FilePath, problem.Line, problem.Character));
    }

    private void OnProblemsChanged(object? sender, EventArgs e) =>
        ScheduleRefresh();

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        var activePath = GetActiveDocumentPath();
        if (string.Equals(activePath, _lastActivePath, StringComparison.OrdinalIgnoreCase))
            return;

        ScheduleRefresh();
    }

    private void OnSettingsChanged(ProblemsSettings settings)
    {
        _settings = settings;
        ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        if (Interlocked.Exchange(ref _refreshPending, 1) == 1)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _refreshPending, 0);
            Refresh();
        });
    }

    private void Refresh()
    {
        var settings = _settings;
        var activePath = GetActiveDocumentPath();
        _lastActivePath = activePath;

        Items.Clear();
        foreach (var item in _problems.GetVisibleProblems(
                     activePath,
                     settings.ShowWarningsFromAllFiles,
                     settings.ShowInformationFromAllFiles,
                     settings.MaxVisibleProblems)
                 .Select(problem => new ProblemItemViewModel(problem)))
        {
            Items.Add(item);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private string? GetActiveDocumentPath() =>
        _workspace.Current.TabSession.ActiveDocument?.Kind == OpenDocumentKind.TextDocument
            ? _workspace.Current.TabSession.ActiveDocument.Path
            : null;

    public void Dispose()
    {
        _problems.Changed -= OnProblemsChanged;
        _workspace.Changed -= OnWorkspaceChanged;
        _settingsSubscription.Dispose();
    }
}
