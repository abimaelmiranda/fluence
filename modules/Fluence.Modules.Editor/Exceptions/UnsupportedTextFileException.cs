using System;
using Fluence.Core.Abstractions.Exceptions;

namespace Fluence.Modules.Editor.Exceptions;

public sealed class UnsupportedTextFileException : FluenceExceptionBase
{
    public UnsupportedTextFileException(string path, string? reason = null)
        : base(CreateMessage(path, reason))
    {
        Path = path;
        Reason = reason ?? "This file does not appear to be a text file.";
    }

    public UnsupportedTextFileException(string path, string? reason, Exception innerException)
        : base(CreateMessage(path, reason), innerException)
    {
        Path = path;
        Reason = reason ?? "This file does not appear to be a text file.";
    }

    public string Path { get; }

    public string Reason { get; }

    public override string UserMessage => Reason;

    private static string CreateMessage(string path, string? reason)
    {
        return reason is null ? $"Unsupported file type: {path}" : $"Unsupported file type: {path}. {reason}";
    }
}