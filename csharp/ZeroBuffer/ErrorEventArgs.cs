using System;

namespace ZeroBuffer;

public class ErrorEventArgs : EventArgs
{
    public Exception Exception { get; }
    public ErrorEventArgs(Exception exception)
    {
        Exception = exception;
    }
}