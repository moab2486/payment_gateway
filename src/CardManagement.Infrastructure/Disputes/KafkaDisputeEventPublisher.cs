using System.Text.Json;
using CardManagement.Application.DTOs;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Disputes;

/// <summary>
/// Kafka-backed implementation of <see cref="IDisputeEventPublisher"/>.
/// Publishes dispute lifecycle events as JSON-serialized EventEnvelope messages.
/// Kafka failures are caught and logged — never thrown to callers.
/// </summary>
public sealed class KafkaDisputeEventPublisher : IDisputeEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly DisputeOptions _options;
    private readonly ILogger<KafkaDisputeEventPublisher> _logger;

    public KafkaDisputeEventPublisher(
        IProducer<string, string> producer,
        IOptions<DisputeOptions> options,
        ILogger<KafkaDisputeEventPublisher> logger)
    {
        _producer = producer ?? throw new ArgumentNullException(nameof(producer));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishDisputeOpenedAsync(DisputeOpenedEvent evt, CancellationToken ct)
    {
        await PublishAsync("dispute-opened", evt.DisputeId.ToString(), evt, evt.TransactionReference, ct);
    }

    public async Task PublishDisputeEscalatedAsync(DisputeEscalatedEvent evt, CancellationToken ct)
    {
        await PublishAsync("dispute-escalated", evt.DisputeId.ToString(), evt, evt.TransactionReference, ct);
    }

    public async Task PublishDisputeResolvedAsync(DisputeResolvedEvent evt, CancellationToken ct)
    {
        await PublishAsync("dispute-resolved", evt.DisputeId.ToString(), evt, evt.TransactionReference, ct);
    }

    public async Task PublishDisputeClosedAsync(DisputeClosedEvent evt, CancellationToken ct)
    {
        await PublishAsync("dispute-closed", evt.DisputeId.ToString(), evt, evt.TransactionReference, ct);
    }

    private async Task PublishAsync(string eventType, string key, object payload, string correlationId, CancellationToken ct)
    {
        try
        {
            var envelope = new EventEnvelope
            {
                EventId = Guid.NewGuid(),
                EventType = eventType,
                CorrelationId = correlationId,
                Timestamp = DateTime.UtcNow,
                Payload = payload
            };

            var json = JsonSerializer.Serialize(envelope);
            var message = new Message<string, string> { Key = key, Value = json };

            await _producer.ProduceAsync(_options.KafkaTopic, message, ct);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogWarning(ex, "Failed to publish {EventType} event to Kafka. Continuing without blocking.", eventType);
        }
        catch (KafkaException ex)
        {
            _logger.LogWarning(ex, "Kafka error publishing {EventType} event. Continuing without blocking.", eventType);
        }
    }

    public void Dispose()
    {
        // Producer is shared; do not dispose here as it's managed by DI container
    }
}
