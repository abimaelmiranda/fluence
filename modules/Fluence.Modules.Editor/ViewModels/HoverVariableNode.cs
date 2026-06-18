using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Fluence.Core.Abstractions.Debugging;
using Fluence.Core.Models.Debugging;
using Fluence.Core.Models.Debugging.Enums;
using Fluence.Core.Services.Debugging;

namespace Fluence.Modules.Editor.ViewModels;

public partial class HoverVariableNode : ObservableObject
{
    private readonly Func<int, CancellationToken, Task<IReadOnlyList<DebugVariable>>> _loadChildren;
    private bool _loaded;

    public HoverVariableNode(DebugVariable variable, Func<int, CancellationToken, Task<IReadOnlyList<DebugVariable>>> loadChildren)
    {
        _loadChildren = loadChildren;
        Name = variable.Name;
        Value = variable.Value;
        Type = string.IsNullOrWhiteSpace(variable.Type) ? null : variable.Type;
        VariablesReference = variable.VariablesReference;

        if (VariablesReference > 0)
            Children.Add(new PlaceholderNode(loadChildren));
    }

    public string Name { get; }
    public string Value { get; }
    public string? Type { get; }
    public int VariablesReference { get; }

    public string DisplayText => Type is null
        ? $"{Name} = {Value}"
        : $"{Name} = {Value} ({Type})";

    public ObservableCollection<HoverVariableNode> Children { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !_loaded && VariablesReference > 0)
            _ = LoadChildrenAsync();
    }

    private async Task LoadChildrenAsync()
    {
        _loaded = true;
        var vars = await _loadChildren(VariablesReference, CancellationToken.None).ConfigureAwait(false);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Children.Clear();
            foreach (var v in vars)
                Children.Add(new HoverVariableNode(v, _loadChildren));
        });
    }

    private sealed class PlaceholderNode(Func<int, CancellationToken, Task<IReadOnlyList<DebugVariable>>> load)
        : HoverVariableNode(new DebugVariable("...", string.Empty, string.Empty), load);
}
