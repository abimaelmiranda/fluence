using System;

namespace Fluence.Core.Infrastructure;

public sealed class TerminalDataEventArgs(byte[] data) : EventArgs
{
    public byte[] Data { get; } = data;
}
