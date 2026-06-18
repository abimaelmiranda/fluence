using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Services.Debugging;

namespace Fluence.Modules.Debug.ViewModels;

public partial class DebugVariableNode : ObservableObject
{
    private readonly Func<int, CancellationToken, Task<IReadOnlyList<DebugVariable>>> _loadChildren;
    private bool _loaded;

    public DebugVariableNode(DebugVariable variable, Func<int, CancellationToken, Task<IReadOnlyList<DebugVariable>>> loadChildren)
    {
        _loadChildren = loadChildren;
        Name = variable.Name;
        Value = variable.Value;
        Type = string.IsNullOrWhiteSpace(variable.Type) ? null : variable.Type;
        VariablesReference = variable.VariablesReference;

        if (VariablesReference > 0)
            Children.Add(new LoadingPlaceholderNode(loadChildren));
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
        var vars = await _loadChildren(VariablesReference, CancellationToken.None).ConfigureAwait(false);
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            Children.Clear();
            foreach (var v in vars)
                Children.Add(new DebugVariableNode(v, _loadChildren));
        });
    }

    // Sentinel node shown while loading children
    private sealed class LoadingPlaceholderNode(Func<int, CancellationToken, Task<IReadOnlyList<DebugVariable>>> loadChildren)
        : DebugVariableNode(new DebugVariable("Loading...", string.Empty, string.Empty), loadChildren)
    {
    }
}
