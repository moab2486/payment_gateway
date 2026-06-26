using System.Text.Json;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Kafka;

/// <summary>
/// Kafka-backed implementation of <see cref="IEventPublisher"/>.
/// Publishes domain events as JSON-serialized <see cref="EventEnvelope"/> messages.
/// All Kafka failures are caught and logged — never thrown to callers.
/// </summary>
public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaEventPublisher> _logger;

    public KafkaEventPublisher(IOptions<KafkaOptions> options, ILogger<KafkaEventPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            ClientId = _options.ClientId,
            Acks = Acks.Leader,
            EnableIdempotence = false,
            MessageTimeoutMs = 5000,
            RequestTimeoutMs = 3000
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    /// <summary>
    /// Internal constructor for unit testing. Accepts a pre-built producer instance.
    /// </summary>
    internal KafkaEventPublisher(IProducer<string, string> producer, IOptions<KafkaOptions> options, ILogger<KafkaEventPublisher> logger)
    {
        _producer = producer;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PublishCardIssuedAsync(CardIssuedEvent cardEvent, CancellationToken ct)
    {
        var envelope = CreateEnvelope("card-issued", cardEvent.CorrelationId, cardEvent);
        await ProduceAsync($"{_options.TopicPrefix}.card-issued", cardEvent.CardId.ToString(), envelope, ct);
    }

    /// <inheritdoc />
    public async Task PublishTransactionAuthorizedAsync(TransactionAuthorizedEvent txEvent, CancellationToken ct)
    {
        var envelope = CreateEnvelope("transaction-authorized", txEvent.CorrelationId, txEvent);
        await ProduceAsync($"{_options.TopicPrefix}.transaction-authorized", txEvent.TransactionId.ToString(), envelope, ct);
    }

    /// <inheritdoc />
    public async Task PublishTransactionReversedAsync(TransactionReversedEvent txEvent, CancellationToken ct)
    {
        var envelope = CreateEnvelope("transaction-reversed", txEvent.CorrelationId, txEvent);
        await ProduceAsync($"{_options.TopicPrefix}.transaction-reversed", txEvent.OriginalTransactionId.ToString(), envelope, ct);
    }

    private static EventEnvelope CreateEnvelope(string eventType, string correlationId, object payload)
    {
        return new EventEnvelope
        {
            EventId = Guid.NewGuid(),
            EventType = eventType,
            CorrelationId = correlationId,
            Timestamp = DateTime.UtcNow,
            Payload = payload
        };
    }

    private async Task ProduceAsync(string topic, string key, EventEnvelope envelope, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(envelope);
            var message = new Message<string, string> { Key = key, Value = json };
            await _producer.ProduceAsync(topic, message, ct);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogWarning(ex, "Failed to publish event to topic {Topic}. Continuing without blocking.", topic);
        }
        catch (KafkaException ex)
        {
            _logger.LogWarning(ex, "Kafka error while publishing to {Topic}. Continuing without blocking.", topic);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _producer?.Dispose();
    }
}
