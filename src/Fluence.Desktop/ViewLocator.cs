using System;
using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Fluence.Core.ViewModels;

namespace Fluence.Desktop;

public sealed class ViewLocator : IDataTemplate
{
    private readonly ConcurrentDictionary<Type, Type?> _cache = new();

    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var viewModelType = param.GetType();
        var viewType = _cache.GetOrAdd(viewModelType, ResolveViewType);

        if (viewType is null)
            return new TextBlock { Text = $"View not found: {viewModelType.Name}" };

        return (Control)Activator.CreateInstance(viewType)!;
    }

    public bool Match(object? data) => data is ViewModelBase;

    private static Type? ResolveViewType(Type viewModelType)
    {
        var viewTypeName = viewModelType.FullName!
            .Replace(".ViewModels.", ".Views.")
            .Replace("ViewModel", "View");

        return viewModelType.Assembly.GetType(viewTypeName);
    }
}
