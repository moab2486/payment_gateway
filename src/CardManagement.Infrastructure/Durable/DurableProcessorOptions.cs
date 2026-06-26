namespace CardManagement.Infrastructure.Durable;

/// <summary>
/// Configuration options for the durable message processor.
/// Bind to the "DurableProcessor" configuration section.
/// </summary>
public sealed class DurableProcessorOptions
{
    public const string SectionName = "DurableProcessor";

    /// <summary>
    /// Kafka consumer group identifier.
    /// </summary>
    public string ConsumerGroup { get; set; } = "payment-processor-group";

    /// <summary>
    /// List of Kafka topics to subscribe to.
    /// </summary>
    public List<string> Topics { get; set; } = new();

    /// <summary>
    /// Maximum number of retries for transient errors before routing to DLQ.
    /// </summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Initial retry delay in milliseconds. Subsequent retries use exponential backoff.
    /// </summary>
    public int InitialRetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Suffix appended to the original topic name to form the dead-letter topic.
    /// </summary>
    public string DeadLetterTopicSuffix { get; set; } = ".dlq";
}
