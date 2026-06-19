using System;

namespace Fluence.Core.Models.Lifecycle;

public sealed class ApplicationShutdownStatusChangedEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}
