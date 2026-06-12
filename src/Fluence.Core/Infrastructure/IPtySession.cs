using System;
using System.IO;

namespace Fluence.Core.Infrastructure;

public interface IPtySession : IDisposable
{
    Stream Input { get; }
    Stream Output { get; }
    bool HasExited { get; }
    event EventHandler? Exited;
    void Resize(int columns, int rows);
}
