namespace CardManagement.Infrastructure.Kafka;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = string.Empty;
    public string TopicPrefix { get; set; } = "cardmgmt.events";
    public string ClientId { get; set; } = "card-management-api";
}
