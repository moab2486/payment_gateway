namespace CardManagement.Infrastructure.Durable;

/// <summary>
/// Marker exception indicating a non-retryable (permanent) processing failure.
/// Messages that fail with this exception are routed to the dead-letter topic.
/// </summary>
public class PermanentErrorException : Exception
{
    public PermanentErrorException()
    {
    }

    public PermanentErrorException(string message)
        : base(message)
    {
    }

    public PermanentErrorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
