using Confluent.Kafka;

namespace CardManagement.Infrastructure.Durable;

/// <summary>
/// Production wrapper around Confluent.Kafka IConsumer.
/// Configured for manual offset commits and earliest auto-offset reset.
/// </summary>
public sealed class KafkaConsumerWrapper : IKafkaConsumerWrapper
{
    private readonly IConsumer<string, string> _consumer;

    public KafkaConsumerWrapper(string bootstrapServers, string consumerGroup)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = consumerGroup,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnablePartitionEof = false
        };

        _consumer = new ConsumerBuilder<string, string>(config).Build();
    }

    public void Subscribe(IEnumerable<string> topics)
    {
        _consumer.Subscribe(topics);
    }

    public ConsumeResult<string, string>? Consume(CancellationToken ct)
    {
        return _consumer.Consume(ct);
    }

    public void Commit(ConsumeResult<string, string> result)
    {
        _consumer.Commit(result);
    }

    public void Close()
    {
        _consumer.Close();
    }

    public void Dispose()
    {
        _consumer?.Dispose();
    }
}
