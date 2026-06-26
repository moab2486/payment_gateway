using System.Text.Json;
using CardManagement.Application.DTOs;
using CardManagement.Infrastructure.Kafka;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Saga;

/// <summary>
/// Kafka-backed implementation of <see cref="ISagaEventPublisher"/>.
/// Publishes saga lifecycle events as JSON-serialized EventEnvelope messages.
/// </summary>
public sealed class KafkaSagaEventPublisher : ISagaEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaSagaEventPublisher> _logger;

    public KafkaSagaEventPublisher(IOptions<KafkaOptions> options, ILogger<KafkaSagaEventPublisher> logger)
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
    internal KafkaSagaEventPublisher(IProducer<string, string> producer, IOptions<KafkaOptions> options, ILogger<KafkaSagaEventPublisher> logger)
    {
        _producer = producer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task PublishSagaCompletedAsync(string transactionReference, CancellationToken ct)
    {
        var payload = new { TransactionReference = transactionReference, CompletedAtUtc = DateTime.UtcNow };
        var envelope = CreateEnvelope("saga-completed", transactionReference, payload);
        await ProduceAsync($"{_options.TopicPrefix}.saga-completed", transactionReference, envelope, ct);
    }

    public async Task PublishSagaRolledBackAsync(string transactionReference, string failedStep, string errorMessage, CancellationToken ct)
    {
        var payload = new
        {
            TransactionReference = transactionReference,
            FailedStep = failedStep,
            ErrorMessage = errorMessage,
            RolledBackAtUtc = DateTime.UtcNow
        };
        var envelope = CreateEnvelope("saga-rolled-back", transactionReference, payload);
        await ProduceAsync($"{_options.TopicPrefix}.saga-rolled-back", transactionReference, envelope, ct);
    }

    public async Task PublishSagaManualInterventionRequiredAsync(string transactionReference, string failedStep, int retriesExhausted, CancellationToken ct)
    {
        var payload = new
        {
            TransactionReference = transactionReference,
            FailedStep = failedStep,
            RetriesExhausted = retriesExhausted,
            FlaggedAtUtc = DateTime.UtcNow
        };
        var envelope = CreateEnvelope("saga-manual-intervention-required", transactionReference, payload);
        await ProduceAsync($"{_options.TopicPrefix}.saga-manual-intervention-required", transactionReference, envelope, ct);
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
            _logger.LogWarning(ex, "Failed to publish saga event to topic {Topic}. Continuing without blocking.", topic);
        }
        catch (KafkaException ex)
        {
            _logger.LogWarning(ex, "Kafka error while publishing saga event to {Topic}. Continuing without blocking.", topic);
        }
    }

    public void Dispose()
    {
        _producer?.Dispose();
    }
}
