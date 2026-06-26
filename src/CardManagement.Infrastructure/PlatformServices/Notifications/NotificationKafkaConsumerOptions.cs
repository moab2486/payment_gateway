namespace CardManagement.Infrastructure.PlatformServices.Notifications;

/// <summary>
/// Configuration options for the notification dispatch Kafka consumer.
/// </summary>
public sealed class NotificationKafkaConsumerOptions
{
    public const string SectionName = "NotificationKafkaConsumer";

    /// <summary>
    /// Kafka bootstrap servers connection string.
    /// </summary>
    public string BootstrapServers { get; set; } = "localhost:9092";

    /// <summary>
    /// Consumer group ID for the notification dispatch consumer.
    /// </summary>
    public string ConsumerGroupId { get; set; } = "notification-dispatch-consumer";

    /// <summary>
    /// Kafka topic to subscribe to for dispatch-requested events.
    /// </summary>
    public string Topic { get; set; } = "platform.notification.dispatch-requested";
}
