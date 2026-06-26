namespace CardManagement.Infrastructure.PlatformServices.AdminConsole;

/// <summary>
/// Configuration options for the admin read model Kafka consumer.
/// </summary>
public sealed class AdminReadModelKafkaConsumerOptions
{
    public const string SectionName = "AdminReadModelKafkaConsumer";

    /// <summary>
    /// Kafka bootstrap servers connection string.
    /// </summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>
    /// Consumer group ID for the admin read model projector consumer.
    /// </summary>
    public string ConsumerGroupId { get; set; } = "admin-readmodel-projector";

    /// <summary>
    /// Kafka topics to subscribe to for domain events that feed read models.
    /// </summary>
    public List<string> Topics { get; set; } = new()
    {
        "cardmgmt.events.payments",
        "platform.reconciliation.batch-completed",
        "cardmgmt.events.disputes",
        "cardmgmt.events.deliveries"
    };

    /// <summary>
    /// Staleness threshold beyond which a read model is considered stale.
    /// </summary>
    public TimeSpan StalenessThreshold { get; set; } = TimeSpan.FromMinutes(5);
}
