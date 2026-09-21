using System.Net;

namespace LedBalloon.Core;

/// <summary>Raised when a device answers but the answer is not something we can use.</summary>
public class WledException : Exception
{
    public WledException(string message) : base(message)
    {
    }

    public WledException(string message, Exception? inner) : base(message, inner)
    {
    }
}

public sealed class WledHttpException : WledException
{
    public WledHttpException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
