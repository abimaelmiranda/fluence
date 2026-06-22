using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Abstractions.Localization;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Services.Debugging;

namespace Fluence.Modules.Debug.ViewModels;

public partial class DebugVariableNode : ObservableObject
{
    private readonly Func<int, CancellationToken, Task<IReadOnlyList<DebugVariable>>> _loadChildren;
    private readonly ILocalizationService _loc;
    private bool _loaded;
    private int _loadGeneration;

    public DebugVariableNode(
        DebugVariable variable,
        Func<int, CancellationToken, Task<IReadOnlyList<DebugVariable>>> loadChildren,
        ILocalizationService loc)
    {
        _loadChildren = loadChildren;
        _loc = loc;
        Name = variable.Name;
        Value = variable.Value;
        Type = string.IsNullOrWhiteSpace(variable.Type) ? null : variable.Type;
        VariablesReference = variable.VariablesReference;

        if (VariablesReference > 0)
            Children.Add(new LoadingPlaceholderNode(loadChildren, loc));
    }

    public string Name { get; }
    public string Value { get; }
    public string? Type { get; }
    public int VariablesReference { get; }
    public bool HasChildren => VariablesReference > 0;

    public string DisplayText => Type is null
        ? $"{Name} = {Value}"
        : $"{Name} = {Value} ({Type})";

    public ObservableCollection<DebugVariableNode> Children { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_loaded && HasChildren)
            _ = LoadChildrenAsync();
    }

    private async Task LoadChildrenAsync()
    {
        _loaded = true;
        var generation = Interlocked.Increment(ref _loadGeneration);
        var vars = await _loadChildren(VariablesReference, CancellationToken.None).ConfigureAwait(false);
        if (Volatile.Read(ref _loadGeneration) != generation) return;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Volatile.Read(ref _loadGeneration) != generation) return;
            Children.Clear();
            foreach (var v in vars)
                Children.Add(new DebugVariableNode(v, _loadChildren, _loc));
        });
    }

    // Sentinel node shown while loading children
    private sealed class LoadingPlaceholderNode(
        Func<int, CancellationToken, Task<IReadOnlyList<DebugVariable>>> loadChildren,
        ILocalizationService loc)
        : DebugVariableNode(new DebugVariable(loc.Get("Debug.Variable.Loading"), string.Empty, string.Empty), loadChildren, loc)
    {
    }
}
