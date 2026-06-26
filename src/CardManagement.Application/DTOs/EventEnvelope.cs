namespace CardManagement.Application.DTOs;

/// <summary>
/// Common envelope wrapping all domain events published to Kafka.
/// </summary>
public record EventEnvelope
{
    /// <summary>Unique event identifier (UUID v4).</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>Event type discriminator (e.g., "card-issued", "transaction-authorized").</summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>Correlation identifier for end-to-end tracing.</summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the event was created.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>Domain-specific event payload serialized as a nested JSON object.</summary>
    public object Payload { get; init; } = new();
}
