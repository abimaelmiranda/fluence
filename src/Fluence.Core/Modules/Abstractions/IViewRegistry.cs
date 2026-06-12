using System;

namespace Fluence.Core.Modules;

public interface IViewRegistry
{
    void Register<TViewModel, TView>()
        where TViewModel : class
        where TView : class;

    Type? Resolve(Type viewModelType);
}
