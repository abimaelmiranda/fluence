using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Services.Debugging;
using Fluence.Core.ViewModels;

namespace Fluence.Modules.Debug.ViewModels;

public sealed partial class DebugWatchEntry : ViewModelBase
{
    private readonly IDebugService _debugService;
    private readonly IDebugStateService _debugState;
    private readonly ILocalizationService _loc;
    private readonly Action<DebugWatchEntry> _remove;

    public DebugWatchEntry(
        string expression,
        Action<DebugWatchEntry> remove,
        IDebugService debugService,
        IDebugStateService debugState,
        ILocalizationService loc)
    {
        Expression = expression;
        _remove = remove;
        _debugService = debugService;
        _debugState = debugState;
        _loc = loc;
    }

    public string Expression { get; }

    // Single-item collection so TreeView can bind directly without converters
    public ObservableCollection<DebugVariableNode> Results { get; } = [];

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _isEvaluating;

    public bool HasResult => Results.Count > 0;

    public bool HasError => Error is not null;

    [RelayCommand]
    private void Remove() => _remove(this);

    public async Task EvaluateAsync()
    {
        if (!_debugState.Snapshot.IsActive || !_debugState.Snapshot.IsStopped)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Results.Clear();
                Error = null;
                OnPropertyChanged(nameof(HasResult));
                OnPropertyChanged(nameof(HasError));
            });
            return;
        }

        IsEvaluating = true;
        try
        {
            var variable = await _debugService.EvaluateAsync(Expression, CancellationToken.None).ConfigureAwait(false);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Results.Clear();
                if (variable is null)
                {
                    Error = "undefined";
                }
                else
                {
                    Results.Add(new DebugVariableNode(variable, _debugService.GetChildVariablesAsync, _loc));
                    Error = null;
                }
                OnPropertyChanged(nameof(HasResult));
                OnPropertyChanged(nameof(HasError));
            });
        }
        catch (Exception ex)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Results.Clear();
                Error = ex.Message;
                OnPropertyChanged(nameof(HasResult));
                OnPropertyChanged(nameof(HasError));
            });
        }
        finally
        {
            IsEvaluating = false;
        }
    }
}
