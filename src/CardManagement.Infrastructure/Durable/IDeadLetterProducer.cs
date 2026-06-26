namespace CardManagement.Infrastructure.Durable;

/// <summary>
/// Abstraction for publishing messages to the dead-letter topic.
/// </summary>
public interface IDeadLetterProducer : IDisposable
{
    /// <summary>
    /// Publish a message to the dead-letter topic for the specified original topic.
    /// </summary>
    /// <param name="originalTopic">The topic the message was originally consumed from.</param>
    /// <param name="key">The message key.</param>
    /// <param name="value">The message value/payload.</param>
    /// <param name="reason">Reason the message was routed to DLQ.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PublishAsync(string originalTopic, string? key, string value, string reason, CancellationToken ct);
}
