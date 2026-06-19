using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Fluence.Core.Abstractions.Tasks;

namespace Fluence.Desktop.Services;

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action, UiDispatchPriority priority = UiDispatchPriority.Background) =>
        Dispatcher.UIThread.Post(action, MapPriority(priority));

    public Task InvokeAsync(Action action) =>
        Dispatcher.UIThread.InvokeAsync(action).GetTask();

    private static DispatcherPriority MapPriority(UiDispatchPriority priority) =>
        priority == UiDispatchPriority.Input
            ? DispatcherPriority.Input
            : DispatcherPriority.Background;
}
