using System.Text.Json;
using CardManagement.Application.PlatformServices.AdminConsole.Ports;
using CardManagement.Infrastructure.Durable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.PlatformServices.AdminConsole;

/// <summary>
/// Background service that consumes domain events from Kafka topics and projects
/// them into admin read models. Uses a dedicated consumer group
/// (<c>admin-readmodel-projector</c>) to maintain independent read-side offsets.
///
/// Consumed topics include:
/// - <c>cardmgmt.events.payments</c> (payment state changes → TransactionSummary)
/// - <c>platform.reconciliation.batch-completed</c> (batch results → ReconciliationStatus)
/// - <c>cardmgmt.events.disputes</c> (dispute lifecycle → DisputeMetrics)
/// - <c>cardmgmt.events.deliveries</c> (delivery outcomes → ChannelHealth)
/// </summary>
public sealed class AdminReadModelKafkaConsumer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AdminReadModelKafkaConsumerOptions _options;
    private readonly ILogger<AdminReadModelKafkaConsumer> _logger;

    public AdminReadModelKafkaConsumer(
        IServiceProvider serviceProvider,
        IOptions<AdminReadModelKafkaConsumerOptions> options,
        ILogger<AdminReadModelKafkaConsumer> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AdminReadModelKafkaConsumer starting. Consumer group: {GroupId}, Topics: [{Topics}].",
            _options.ConsumerGroupId, string.Join(", ", _options.Topics));

        // Run the consumer loop on a background thread to avoid blocking startup
        await Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);
    }

    private async Task ConsumeLoopAsync(CancellationToken stoppingToken)
    {
        IKafkaConsumerWrapper? consumer = null;

        try
        {
            consumer = CreateConsumer();
            consumer.Subscribe(_options.Topics);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(stoppingToken);
                    if (result is null)
                        continue;

                    await ProcessMessageAsync(result.Message.Value, stoppingToken);
                    consumer.Commit(result);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing Kafka message for read model projection. Continuing to next message.");
                    // Continue consuming — individual message failures don't stop the consumer
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "AdminReadModelKafkaConsumer encountered a fatal error.");
        }
        finally
        {
            consumer?.Close();
            consumer?.Dispose();
            _logger.LogInformation("AdminReadModelKafkaConsumer stopped.");
        }
    }

    private async Task ProcessMessageAsync(string messageValue, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(messageValue))
        {
            _logger.LogDebug("Received empty Kafka message. Skipping.");
            return;
        }

        // Extract event type from the envelope
        EventEnvelopeMinimal? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelopeMinimal>(messageValue, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Kafka message as event envelope. Skipping.");
            return;
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.EventType))
        {
            _logger.LogDebug("Deserialized envelope has no event type. Skipping.");
            return;
        }

        _logger.LogDebug(
            "Processing event for read model projection: EventType={EventType}.",
            envelope.EventType);

        // Resolve the projector from a scoped service provider
        using var scope = _serviceProvider.CreateScope();
        var projector = scope.ServiceProvider.GetRequiredService<IAdminReadModelProjector>();

        // The event payload is the serialized data within the envelope.
        // Pass the full payload field (or the entire message if payload is absent).
        var payloadToProject = envelope.Payload ?? messageValue;

        await projector.ProjectEventAsync(envelope.EventType, payloadToProject, ct);
    }

    /// <summary>
    /// Creates the Kafka consumer wrapper.
    /// </summary>
    private IKafkaConsumerWrapper CreateConsumer()
    {
        return new KafkaConsumerWrapper(_options.BootstrapServers, _options.ConsumerGroupId);
    }

    /// <summary>
    /// Minimal envelope DTO to extract event type and payload from Kafka messages.
    /// </summary>
    private sealed class EventEnvelopeMinimal
    {
        public string? EventType { get; set; }
        public string? Payload { get; set; }
    }
}
