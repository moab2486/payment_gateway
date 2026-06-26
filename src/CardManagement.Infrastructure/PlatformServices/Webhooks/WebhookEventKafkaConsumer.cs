using System.Text.Json;
using CardManagement.Application.DTOs;
using CardManagement.Application.PlatformServices.Webhooks.DTOs;
using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Domain.PlatformServices.Webhooks;
using CardManagement.Infrastructure.Durable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.PlatformServices.Webhooks;

/// <summary>
/// Background service that consumes events from Kafka <c>cardmgmt.events.*</c> topics
/// and dispatches matching events to webhook subscriptions via the delivery worker pool.
/// 
/// For each consumed event:
/// 1. Deserialize the <see cref="EventEnvelope"/> from the Kafka message value
/// 2. Query active webhook subscriptions matching the event type
/// 3. Create a <see cref="WebhookDelivery"/> for each matching subscription
/// 4. Enqueue a <see cref="WebhookDeliveryTask"/> to the worker pool for async delivery
/// </summary>
public sealed class WebhookEventKafkaConsumer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly WebhookKafkaConsumerOptions _options;
    private readonly ILogger<WebhookEventKafkaConsumer> _logger;

    public WebhookEventKafkaConsumer(
        IServiceProvider serviceProvider,
        IOptions<WebhookKafkaConsumerOptions> options,
        ILogger<WebhookEventKafkaConsumer> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "WebhookEventKafkaConsumer starting. Consumer group: {GroupId}, Topics: [{Topics}].",
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
                    _logger.LogError(ex, "Error processing Kafka message. Continuing to next message.");
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
            _logger.LogCritical(ex, "WebhookEventKafkaConsumer encountered a fatal error.");
        }
        finally
        {
            consumer?.Close();
            consumer?.Dispose();
            _logger.LogInformation("WebhookEventKafkaConsumer stopped.");
        }
    }

    private async Task ProcessMessageAsync(string messageValue, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(messageValue))
        {
            _logger.LogDebug("Received empty Kafka message. Skipping.");
            return;
        }

        EventEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope>(messageValue, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Kafka message as EventEnvelope. Skipping.");
            return;
        }

        if (envelope is null || string.IsNullOrWhiteSpace(envelope.EventType))
        {
            _logger.LogDebug("Deserialized envelope has no event type. Skipping.");
            return;
        }

        _logger.LogDebug(
            "Processing event: EventId={EventId}, EventType={EventType}.",
            envelope.EventId, envelope.EventType);

        using var scope = _serviceProvider.CreateScope();
        var subscriptionRepository = scope.ServiceProvider.GetRequiredService<IWebhookSubscriptionRepository>();
        var deliveryRepository = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryRepository>();
        var workerPool = _serviceProvider.GetRequiredService<WebhookDeliveryWorkerPool>();

        // Find all active subscriptions matching this event type
        var subscriptions = await subscriptionRepository.GetActiveByEventTypeAsync(envelope.EventType, ct);

        if (subscriptions.Count == 0)
        {
            _logger.LogDebug("No active subscriptions for event type '{EventType}'.", envelope.EventType);
            return;
        }

        _logger.LogInformation(
            "Dispatching event {EventType} to {Count} subscription(s).",
            envelope.EventType, subscriptions.Count);

        // Build the webhook payload as a JSON string
        var payload = BuildWebhookPayload(envelope);

        foreach (var subscription in subscriptions)
        {
            // Skip suspended or deactivated subscriptions (defensive — query should only return active)
            if (subscription.Status != SubscriptionStatus.Active)
                continue;

            try
            {
                // Create a new pending delivery record
                var delivery = WebhookDelivery.Create(
                    subscriptionId: subscription.Id,
                    eventType: envelope.EventType,
                    payload: payload);

                await deliveryRepository.CreateAsync(delivery, ct);

                // Enqueue for async worker pool processing
                var task = new WebhookDeliveryTask(DeliveryId: delivery.Id, IsRetry: false);
                await workerPool.EnqueueAsync(task, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to create delivery for subscription {SubscriptionId} and event {EventType}.",
                    subscription.Id, envelope.EventType);
                // Continue dispatching to other subscriptions
            }
        }
    }

    /// <summary>
    /// Builds the webhook payload JSON string from the event envelope.
    /// The payload includes the event type, data, timestamp, and a unique delivery ID.
    /// </summary>
    private static string BuildWebhookPayload(EventEnvelope envelope)
    {
        var payloadObject = new
        {
            id = Guid.NewGuid().ToString(),
            event_type = envelope.EventType,
            timestamp = envelope.Timestamp.ToString("O"),
            data = envelope.Payload
        };

        return JsonSerializer.Serialize(payloadObject, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
    }

    /// <summary>
    /// Creates the Kafka consumer wrapper. Virtual for testability.
    /// </summary>
    private IKafkaConsumerWrapper CreateConsumer()
    {
        return new KafkaConsumerWrapper(_options.BootstrapServers, _options.ConsumerGroupId);
    }
}
