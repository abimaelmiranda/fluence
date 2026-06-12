using System;

namespace Fluence.Core.Infrastructure;

public sealed class TerminalLineEventArgs(string line, bool isError) : EventArgs
{
    public string Line { get; } = line;
    public bool IsError { get; } = isError;
}
