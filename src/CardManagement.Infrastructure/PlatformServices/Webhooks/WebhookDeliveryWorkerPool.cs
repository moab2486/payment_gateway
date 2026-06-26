using CardManagement.Application.PlatformServices.Webhooks.DTOs;
using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Domain.PlatformServices.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.PlatformServices.Webhooks;

/// <summary>
/// Webhook delivery worker pool that wraps the generic <see cref="WorkerPool{TTask}"/>
/// infrastructure to provide concurrent webhook delivery processing.
/// 
/// Each task: load delivery → invoke delivery engine → handle result (retry/DLQ/success).
/// The <see cref="WebhookDeliveryEngine"/> already handles delivery attempt recording,
/// retry scheduling, DLQ routing, and subscription health tracking internally.
/// </summary>
public sealed class WebhookDeliveryWorkerPool
{
    private readonly WorkerPool<WebhookDeliveryTask> _innerPool;
    private readonly ILogger<WebhookDeliveryWorkerPool> _logger;

    public WebhookDeliveryWorkerPool(
        WorkerPool<WebhookDeliveryTask> innerPool,
        ILogger<WebhookDeliveryWorkerPool> logger)
    {
        _innerPool = innerPool ?? throw new ArgumentNullException(nameof(innerPool));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Enqueues a webhook delivery task for processing by a worker.
    /// </summary>
    public async Task EnqueueAsync(WebhookDeliveryTask task, CancellationToken ct)
    {
        _logger.LogInformation(
            "Enqueuing webhook delivery task for delivery {DeliveryId}, IsRetry={IsRetry}.",
            task.DeliveryId, task.IsRetry);

        await _innerPool.EnqueueAsync(task, ct);
    }

    /// <summary>
    /// Gets the number of workers currently active.
    /// </summary>
    public int ActiveWorkerCount => _innerPool.ActiveWorkerCount;

    /// <summary>
    /// Gets the number of pending tasks in the channel.
    /// </summary>
    public int PendingTaskCount => _innerPool.PendingTaskCount;

    /// <summary>
    /// Creates the handler delegate that processes each <see cref="WebhookDeliveryTask"/>.
    /// This factory is used during DI registration to wire the delivery pipeline.
    /// </summary>
    internal static Func<WebhookDeliveryTask, CancellationToken, Task> CreateHandler(IServiceProvider sp)
    {
        return async (task, ct) =>
        {
            using var scope = sp.CreateScope();
            var scopedProvider = scope.ServiceProvider;

            var deliveryRepository = scopedProvider.GetRequiredService<IWebhookDeliveryRepository>();
            var deliveryEngine = scopedProvider.GetRequiredService<IWebhookDeliveryEngine>();
            var subscriptionRepository = scopedProvider.GetRequiredService<IWebhookSubscriptionRepository>();
            var logger = scopedProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("WebhookDeliveryWorkerPool.Handler");

            logger.LogDebug(
                "Processing webhook delivery task: DeliveryId={DeliveryId}, IsRetry={IsRetry}.",
                task.DeliveryId, task.IsRetry);

            var delivery = await deliveryRepository.GetByIdAsync(task.DeliveryId, ct);
            if (delivery is null)
            {
                logger.LogError("Delivery {DeliveryId} not found. Skipping task.", task.DeliveryId);
                return;
            }

            // Skip if delivery is already in a terminal state
            if (delivery.Status is DeliveryStatus.Delivered or DeliveryStatus.DeadLettered)
            {
                logger.LogInformation(
                    "Delivery {DeliveryId} is already in terminal state {Status}. Skipping.",
                    task.DeliveryId, delivery.Status);
                return;
            }

            // Delegate to the delivery engine which handles:
            // - HMAC signing and HTTP dispatch
            // - Recording attempt results
            // - Retry scheduling with exponential backoff
            // - DLQ routing on exhausted retries
            // - Subscription consecutive failure tracking and auto-suspension
            await deliveryEngine.DeliverAsync(delivery, ct);

            // After delivery, check if subscription was suspended and notify
            var subscription = await subscriptionRepository.GetByIdAsync(delivery.SubscriptionId, ct);
            if (subscription?.Status == SubscriptionStatus.Suspended)
            {
                await NotifySubscriptionSuspendedAsync(scopedProvider, subscription, logger, ct);
            }
        };
    }

    /// <summary>
    /// Sends a notification when a subscription is suspended due to consecutive failures.
    /// Uses the notification dispatcher if available, otherwise logs the event.
    /// </summary>
    private static async Task NotifySubscriptionSuspendedAsync(
        IServiceProvider sp,
        WebhookSubscription subscription,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var notificationDispatcher = sp.GetService<Application.PlatformServices.Notifications.Ports.INotificationDispatcher>();
            if (notificationDispatcher is null)
            {
                logger.LogWarning(
                    "Notification dispatcher not available. Cannot notify about subscription {SubscriptionId} suspension.",
                    subscription.Id);
                return;
            }

            var request = new Application.PlatformServices.Notifications.DTOs.NotificationRequest(
                RecipientId: subscription.MerchantId.ToString(),
                RecipientAddress: string.Empty, // Resolved by notification service from preferences
                TemplateId: Guid.Empty, // Will be resolved by template name in a real setup
                Variables: new Dictionary<string, string>
                {
                    ["subscription_id"] = subscription.Id.ToString(),
                    ["destination_url"] = subscription.DestinationUrl,
                    ["consecutive_failures"] = subscription.ConsecutiveFailures.ToString(),
                    ["suspended_at"] = DateTime.UtcNow.ToString("O")
                });

            await notificationDispatcher.DispatchAsync(request, ct);

            logger.LogInformation(
                "Suspension notification dispatched for subscription {SubscriptionId} (merchant {MerchantId}).",
                subscription.Id, subscription.MerchantId);
        }
        catch (Exception ex)
        {
            // Notification failure should not block the delivery pipeline
            logger.LogError(ex,
                "Failed to dispatch suspension notification for subscription {SubscriptionId}.",
                subscription.Id);
        }
    }
}
