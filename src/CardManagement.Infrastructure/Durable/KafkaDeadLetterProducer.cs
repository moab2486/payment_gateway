using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Durable;

/// <summary>
/// Production implementation that publishes dead-letter messages to Kafka.
/// </summary>
public sealed class KafkaDeadLetterProducer : IDeadLetterProducer
{
    private readonly IProducer<string, string> _producer;
    private readonly DurableProcessorOptions _options;

    public KafkaDeadLetterProducer(string bootstrapServers, IOptions<DurableProcessorOptions> options)
    {
        _options = options.Value;

        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync(string originalTopic, string? key, string value, string reason, CancellationToken ct)
    {
        var dlqTopic = originalTopic + _options.DeadLetterTopicSuffix;

        var dlqPayload = JsonSerializer.Serialize(new
        {
            OriginalTopic = originalTopic,
            OriginalKey = key,
            OriginalValue = value,
            Reason = reason,
            Timestamp = DateTime.UtcNow
        });

        var message = new Message<string, string>
        {
            Key = key ?? string.Empty,
            Value = dlqPayload
        };

        await _producer.ProduceAsync(dlqTopic, message, ct);
    }

    public void Dispose()
    {
        _producer?.Dispose();
    }
}
