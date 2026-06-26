namespace CardManagement.Infrastructure.Durable;

/// <summary>
/// Handles individual messages consumed by the durable processor.
/// Implementations should throw <see cref="PermanentErrorException"/> for non-retryable failures.
/// Any other exception is treated as a transient error and will be retried.
/// </summary>
public interface IMessageHandler
{
    /// <summary>
    /// Process a consumed message.
    /// </summary>
    /// <param name="topic">The topic the message was consumed from.</param>
    /// <param name="key">The message key (used for partition ordering).</param>
    /// <param name="value">The message value/payload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task HandleAsync(string topic, string? key, string value, CancellationToken ct);
}
