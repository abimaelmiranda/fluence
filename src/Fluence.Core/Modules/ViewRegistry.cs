using System;
using System.Collections.Generic;

namespace Fluence.Core.Modules;

public sealed class ViewRegistry : IViewRegistry
{
    private readonly Dictionary<Type, Type> _map = new();

    public void Register<TViewModel, TView>()
        where TViewModel : class
        where TView : class
    {
        _map[typeof(TViewModel)] = typeof(TView);
    }

    public Type? Resolve(Type viewModelType)
        => _map.TryGetValue(viewModelType, out var viewType) ? viewType : null;
}
