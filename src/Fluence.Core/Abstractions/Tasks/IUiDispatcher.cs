using System;
using System.Threading.Tasks;

namespace Fluence.Core.Abstractions.Tasks;

public enum UiDispatchPriority
{
    Input,
    Background,
}

public interface IUiDispatcher
{
    bool CheckAccess();
    void Post(Action action, UiDispatchPriority priority = UiDispatchPriority.Background);
    Task InvokeAsync(Action action);
}
