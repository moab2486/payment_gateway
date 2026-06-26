using System.Text.Json;
using CardManagement.Application.PlatformServices.Reconciliation.DTOs;
using CardManagement.Application.PlatformServices.Reconciliation.Ports;
using CardManagement.Infrastructure.Kafka;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.PlatformServices.Reconciliation;

/// <summary>
/// Kafka-backed implementation of IReconciliationEventPublisher.
/// Publishes reconciliation domain events to platform.reconciliation.* topics.
/// </summary>
public sealed class KafkaReconciliationEventPublisher : IReconciliationEventPublisher, IDisposable
{
    private const string AdjustmentCreatedTopic = "platform.reconciliation.adjustment-created";
    private const string BatchCompletedTopic = "platform.reconciliation.batch-completed";

    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaReconciliationEventPublisher> _logger;

    public KafkaReconciliationEventPublisher(
        IOptions<KafkaOptions> options,
        ILogger<KafkaReconciliationEventPublisher> logger)
    {
        var kafkaOptions = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var config = new ProducerConfig
        {
            BootstrapServers = kafkaOptions.BootstrapServers,
            ClientId = $"{kafkaOptions.ClientId}-reconciliation",
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
    internal KafkaReconciliationEventPublisher(
        IProducer<string, string> producer,
        ILogger<KafkaReconciliationEventPublisher> logger)
    {
        _producer = producer ?? throw new ArgumentNullException(nameof(producer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task PublishAdjustmentCreatedAsync(AdjustmentCreatedEvent @event, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(@event);
        var message = new Message<string, string>
        {
            Key = @event.AdjustmentId.ToString(),
            Value = json
        };

        try
        {
            await _producer.ProduceAsync(AdjustmentCreatedTopic, message, ct);
            _logger.LogDebug(
                "Published adjustment-created event for adjustment {AdjustmentId} to topic {Topic}.",
                @event.AdjustmentId, AdjustmentCreatedTopic);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogWarning(ex,
                "Failed to publish adjustment-created event for adjustment {AdjustmentId} to topic {Topic}. Continuing without blocking.",
                @event.AdjustmentId, AdjustmentCreatedTopic);
        }
        catch (KafkaException ex)
        {
            _logger.LogWarning(ex,
                "Kafka error while publishing adjustment-created event to {Topic}. Continuing without blocking.",
                AdjustmentCreatedTopic);
        }
    }

    /// <inheritdoc />
    public async Task PublishBatchCompletedAsync(Guid batchId, CancellationToken ct)
    {
        var payload = new { BatchId = batchId, CompletedAtUtc = DateTime.UtcNow };
        var json = JsonSerializer.Serialize(payload);
        var message = new Message<string, string>
        {
            Key = batchId.ToString(),
            Value = json
        };

        try
        {
            await _producer.ProduceAsync(BatchCompletedTopic, message, ct);
            _logger.LogDebug(
                "Published batch-completed event for batch {BatchId} to topic {Topic}.",
                batchId, BatchCompletedTopic);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogWarning(ex,
                "Failed to publish batch-completed event for batch {BatchId} to topic {Topic}. Continuing without blocking.",
                batchId, BatchCompletedTopic);
        }
        catch (KafkaException ex)
        {
            _logger.LogWarning(ex,
                "Kafka error while publishing batch-completed event to {Topic}. Continuing without blocking.",
                BatchCompletedTopic);
        }
    }

    public void Dispose()
    {
        _producer?.Dispose();
    }
}
