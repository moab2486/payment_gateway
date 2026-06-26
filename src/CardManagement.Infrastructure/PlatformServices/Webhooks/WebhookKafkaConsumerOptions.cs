namespace CardManagement.Infrastructure.PlatformServices.Webhooks;

/// <summary>
/// Configuration options for the webhook event Kafka consumer.
/// </summary>
public sealed class WebhookKafkaConsumerOptions
{
    public const string SectionName = "WebhookKafkaConsumer";

    /// <summary>
    /// Kafka bootstrap servers connection string.
    /// </summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>
    /// Consumer group ID for the webhook event consumer.
    /// </summary>
    public string ConsumerGroupId { get; set; } = "webhook-delivery-consumer";

    /// <summary>
    /// Kafka topics to subscribe to. Uses pattern matching for cardmgmt.events.* topics.
    /// </summary>
    public string[] Topics { get; set; } = new[]
    {
        "cardmgmt.events.payment",
        "cardmgmt.events.card",
        "cardmgmt.events.dispute",
        "cardmgmt.events.account"
    };
}
