using System;

namespace Fluence.Core.Abstractions.Modules;

public interface IViewRegistry
{
    void Register<TViewModel, TView>()
        where TViewModel : class
        where TView : class;

    Type? Resolve(Type viewModelType);
}
