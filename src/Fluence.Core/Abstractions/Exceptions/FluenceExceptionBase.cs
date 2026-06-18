using System;

namespace Fluence.Core.Abstractions.Exceptions;

public abstract class FluenceExceptionBase : Exception
{
    protected FluenceExceptionBase(string message)
        : base(message)
    {
    }

    protected FluenceExceptionBase(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public virtual string UserMessage => Message;
}
