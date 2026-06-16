using System;
using System.Collections.Generic;

namespace Fluence.Core.Services;

public sealed class ObservableValue<T> : IObservable<T>
{
    private readonly object _gate = new();
    private readonly List<IObserver<T>> _observers = [];
    private T? _lastValue;
    private bool _hasLastValue;

    public IDisposable Subscribe(IObserver<T> observer)
    {
        lock (_gate)
        {
            _observers.Add(observer);
            if (_hasLastValue)
                observer.OnNext(_lastValue!);
        }

        return new Subscription(this, observer);
    }

    public void Publish(T value)
    {
        IObserver<T>[] observers;
        lock (_gate)
        {
            _lastValue = value;
            _hasLastValue = true;
            observers = _observers.ToArray();
        }

        foreach (var observer in observers)
            observer.OnNext(value);
    }

    private void Unsubscribe(IObserver<T> observer)
    {
        lock (_gate)
        {
            _observers.Remove(observer);
        }
    }

    private sealed class Subscription(ObservableValue<T> source, IObserver<T> observer) : IDisposable
    {
        public void Dispose() => source.Unsubscribe(observer);
    }
}
