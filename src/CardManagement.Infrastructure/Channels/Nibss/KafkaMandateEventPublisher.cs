using System.Text.Json;
using CardManagement.Application.DTOs;
using CardManagement.Infrastructure.Kafka;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// Kafka-backed implementation of <see cref="IMandateEventPublisher"/>.
/// Publishes mandate lifecycle events as JSON-serialized EventEnvelope messages.
/// </summary>
public sealed class KafkaMandateEventPublisher : IMandateEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaMandateEventPublisher> _logger;

    public KafkaMandateEventPublisher(IOptions<KafkaOptions> options, ILogger<KafkaMandateEventPublisher> logger)
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
    internal KafkaMandateEventPublisher(IProducer<string, string> producer, IOptions<KafkaOptions> options, ILogger<KafkaMandateEventPublisher> logger)
    {
        _producer = producer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task PublishMandateCreatedAsync(string mandateReference, string debtorAccount, string creditorAccount, decimal amount, CancellationToken ct)
    {
        var payload = new
        {
            MandateReference = mandateReference,
            DebtorAccount = debtorAccount,
            CreditorAccount = creditorAccount,
            Amount = amount,
            CreatedAtUtc = DateTime.UtcNow
        };
        var envelope = CreateEnvelope("mandate-created", mandateReference, payload);
        await ProduceAsync($"{_options.TopicPrefix}.mandate-created", mandateReference, envelope, ct);
    }

    public async Task PublishMandateActivatedAsync(string mandateReference, CancellationToken ct)
    {
        var payload = new
        {
            MandateReference = mandateReference,
            ActivatedAtUtc = DateTime.UtcNow
        };
        var envelope = CreateEnvelope("mandate-activated", mandateReference, payload);
        await ProduceAsync($"{_options.TopicPrefix}.mandate-activated", mandateReference, envelope, ct);
    }

    public async Task PublishMandateDebitedAsync(string mandateReference, string transactionReference, decimal amount, CancellationToken ct)
    {
        var payload = new
        {
            MandateReference = mandateReference,
            TransactionReference = transactionReference,
            Amount = amount,
            DebitedAtUtc = DateTime.UtcNow
        };
        var envelope = CreateEnvelope("mandate-debited", mandateReference, payload);
        await ProduceAsync($"{_options.TopicPrefix}.mandate-debited", mandateReference, envelope, ct);
    }

    public async Task PublishMandateFailedAsync(string mandateReference, string transactionReference, string errorCode, string errorMessage, CancellationToken ct)
    {
        var payload = new
        {
            MandateReference = mandateReference,
            TransactionReference = transactionReference,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            FailedAtUtc = DateTime.UtcNow
        };
        var envelope = CreateEnvelope("mandate-failed", mandateReference, payload);
        await ProduceAsync($"{_options.TopicPrefix}.mandate-failed", mandateReference, envelope, ct);
    }

    public async Task PublishMandateCancelledAsync(string mandateReference, CancellationToken ct)
    {
        var payload = new
        {
            MandateReference = mandateReference,
            CancelledAtUtc = DateTime.UtcNow
        };
        var envelope = CreateEnvelope("mandate-cancelled", mandateReference, payload);
        await ProduceAsync($"{_options.TopicPrefix}.mandate-cancelled", mandateReference, envelope, ct);
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
            _logger.LogWarning(ex, "Failed to publish mandate event to topic {Topic}. Continuing without blocking.", topic);
        }
        catch (KafkaException ex)
        {
            _logger.LogWarning(ex, "Kafka error while publishing mandate event to {Topic}. Continuing without blocking.", topic);
        }
    }

    public void Dispose()
    {
        _producer?.Dispose();
    }
}
