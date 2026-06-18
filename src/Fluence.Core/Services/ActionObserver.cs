using System;

namespace Fluence.Core.Services;

public sealed class ActionObserver<T>(Action<T> onNext, Action<Exception>? onError = null) : IObserver<T>
{
    public void OnCompleted() { }

    public void OnError(Exception error) => onError?.Invoke(error);

    public void OnNext(T value) => onNext(value);
}
