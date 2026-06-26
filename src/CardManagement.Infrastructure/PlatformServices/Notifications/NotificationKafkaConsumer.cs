using System.Text.Json;
using CardManagement.Infrastructure.Durable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.PlatformServices.Notifications;

/// <summary>
/// Background service that consumes notification dispatch requests from the
/// <c>platform.notification.dispatch-requested</c> Kafka topic and enqueues them
/// into the <see cref="NotificationWorkerPool"/> for async processing.
/// 
/// Message format expected (JSON):
/// {
///   "recipientId": "string",
///   "recipientAddress": "string",
///   "templateId": "guid",
///   "variables": { "key": "value", ... }
/// }
/// </summary>
public sealed class NotificationKafkaConsumer : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly NotificationKafkaConsumerOptions _options;
    private readonly ILogger<NotificationKafkaConsumer> _logger;

    public NotificationKafkaConsumer(
        IServiceProvider serviceProvider,
        IOptions<NotificationKafkaConsumerOptions> options,
        ILogger<NotificationKafkaConsumer> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "NotificationKafkaConsumer starting. Consumer group: {GroupId}, Topic: {Topic}.",
            _options.ConsumerGroupId, _options.Topic);

        // Run the consumer loop on a background thread to avoid blocking startup
        await Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);
    }

    private async Task ConsumeLoopAsync(CancellationToken stoppingToken)
    {
        IKafkaConsumerWrapper? consumer = null;

        try
        {
            consumer = CreateConsumer();
            consumer.Subscribe(new[] { _options.Topic });

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
            _logger.LogCritical(ex, "NotificationKafkaConsumer encountered a fatal error.");
        }
        finally
        {
            consumer?.Close();
            consumer?.Dispose();
            _logger.LogInformation("NotificationKafkaConsumer stopped.");
        }
    }

    private async Task ProcessMessageAsync(string messageValue, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(messageValue))
        {
            _logger.LogDebug("Received empty Kafka message. Skipping.");
            return;
        }

        NotificationDispatchMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<NotificationDispatchMessage>(messageValue, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize Kafka message as NotificationDispatchMessage. Skipping.");
            return;
        }

        if (message is null || string.IsNullOrWhiteSpace(message.RecipientId) || message.TemplateId == Guid.Empty)
        {
            _logger.LogDebug("Deserialized message has missing required fields (recipientId or templateId). Skipping.");
            return;
        }

        _logger.LogDebug(
            "Processing notification dispatch message: RecipientId={RecipientId}, TemplateId={TemplateId}.",
            message.RecipientId, message.TemplateId);

        var workerPool = _serviceProvider.GetRequiredService<NotificationWorkerPool>();

        var task = new NotificationDispatchTask(
            RecipientId: message.RecipientId,
            RecipientAddress: message.RecipientAddress ?? string.Empty,
            TemplateId: message.TemplateId,
            Variables: message.Variables ?? new Dictionary<string, string>());

        await workerPool.EnqueueAsync(task, ct);
    }

    /// <summary>
    /// Creates the Kafka consumer wrapper.
    /// </summary>
    private IKafkaConsumerWrapper CreateConsumer()
    {
        return new KafkaConsumerWrapper(_options.BootstrapServers, _options.ConsumerGroupId);
    }

    /// <summary>
    /// Internal DTO for deserializing Kafka notification dispatch messages.
    /// </summary>
    private sealed class NotificationDispatchMessage
    {
        public string RecipientId { get; set; } = string.Empty;
        public string? RecipientAddress { get; set; }
        public Guid TemplateId { get; set; }
        public Dictionary<string, string>? Variables { get; set; }
    }
}
