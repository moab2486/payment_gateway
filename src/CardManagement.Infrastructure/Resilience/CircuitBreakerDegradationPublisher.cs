using System.Text.Json;
using CardManagement.Application.DTOs;
using CardManagement.Domain.Enums;
using CardManagement.Infrastructure.Kafka;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Resilience;

/// <summary>
/// Monitors circuit breaker state changes across channels and publishes degradation events
/// to Kafka when all instances of a channel enter the Open state.
/// </summary>
public sealed class CircuitBreakerDegradationPublisher : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaOptions _kafkaOptions;
    private readonly ILogger<CircuitBreakerDegradationPublisher> _logger;
    private readonly ICircuitBreakerRegistryInternal _registry;

    public CircuitBreakerDegradationPublisher(
        ICircuitBreakerRegistryInternal registry,
        IOptions<KafkaOptions> kafkaOptions,
        ILogger<CircuitBreakerDegradationPublisher> logger)
    {
        _registry = registry;
        _kafkaOptions = kafkaOptions.Value;
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = _kafkaOptions.BootstrapServers,
            ClientId = $"{_kafkaOptions.ClientId}-circuit-breaker",
            Acks = Acks.Leader,
            MessageTimeoutMs = 5000,
            RequestTimeoutMs = 3000
        };

        _producer = new ProducerBuilder<string, string>(config).Build();

        _registry.OnChannelStateChanged += HandleStateChanged;
    }

    /// <summary>
    /// Internal constructor for testing with a pre-built producer.
    /// </summary>
    internal CircuitBreakerDegradationPublisher(
        ICircuitBreakerRegistryInternal registry,
        IProducer<string, string> producer,
        IOptions<KafkaOptions> kafkaOptions,
        ILogger<CircuitBreakerDegradationPublisher> logger)
    {
        _registry = registry;
        _producer = producer;
        _kafkaOptions = kafkaOptions.Value;
        _logger = logger;

        _registry.OnChannelStateChanged += HandleStateChanged;
    }

    private void HandleStateChanged(PaymentChannel channel, CircuitBreakerState previousState, CircuitBreakerState newState)
    {
        if (newState == CircuitBreakerState.Open)
        {
            // Check if all channels are open - for single-instance per channel, the channel entering Open triggers this
            _ = PublishDegradationEventAsync(channel);
        }
    }

    private async Task PublishDegradationEventAsync(PaymentChannel channel)
    {
        try
        {
            var degradationEvent = new
            {
                EventId = Guid.NewGuid(),
                EventType = "channel-degradation",
                Channel = channel.ToString(),
                Timestamp = DateTime.UtcNow,
                State = CircuitBreakerState.Open.ToString(),
                Message = $"Payment channel {channel} has entered Open state. All requests to this channel will be rejected."
            };

            var topic = $"{_kafkaOptions.TopicPrefix}.channel-degradation";
            var json = JsonSerializer.Serialize(degradationEvent);
            var message = new Message<string, string> { Key = channel.ToString(), Value = json };

            await _producer.ProduceAsync(topic, message);
            _logger.LogWarning("Published degradation event for channel {Channel}", channel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish degradation event for channel {Channel}. Continuing without blocking.", channel);
        }
    }

    public void Dispose()
    {
        _registry.OnChannelStateChanged -= HandleStateChanged;
        _producer?.Dispose();
    }
}
