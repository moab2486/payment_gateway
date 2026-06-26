using CardManagement.Application.PlatformServices.Notifications.Commands;
using CardManagement.Application.PlatformServices.Notifications.Ports;
using CardManagement.Domain.PlatformServices.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.PlatformServices.Notifications;

/// <summary>
/// Notification worker pool that wraps the generic <see cref="WorkerPool{TTask}"/>
/// infrastructure to provide concurrent notification dispatch processing.
/// 
/// Each task: resolve preferences → render template → deliver via primary channel → fallback on failure → log result.
/// The <see cref="DispatchNotificationCommandHandler"/> implements the full dispatch pipeline
/// including channel resolution, fallback logic, and delivery log creation.
/// </summary>
public sealed class NotificationWorkerPool
{
    private readonly WorkerPool<NotificationDispatchTask> _innerPool;
    private readonly ILogger<NotificationWorkerPool> _logger;

    public NotificationWorkerPool(
        WorkerPool<NotificationDispatchTask> innerPool,
        ILogger<NotificationWorkerPool> logger)
    {
        _innerPool = innerPool ?? throw new ArgumentNullException(nameof(innerPool));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Enqueues a notification dispatch task for processing by a worker.
    /// </summary>
    public async Task EnqueueAsync(NotificationDispatchTask task, CancellationToken ct)
    {
        _logger.LogInformation(
            "Enqueuing notification dispatch task for recipient {RecipientId}, template {TemplateId}.",
            task.RecipientId, task.TemplateId);

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
    /// Creates the handler delegate that processes each <see cref="NotificationDispatchTask"/>.
    /// This factory is used during DI registration to wire the notification dispatch pipeline.
    /// 
    /// Each task is converted to a <see cref="DispatchNotificationCommand"/> and delegated to
    /// the command handler, which implements:
    /// - Recipient preference resolution (primary/fallback channel)
    /// - Template rendering with variable substitution
    /// - Primary channel delivery attempt
    /// - Fallback channel escalation on primary failure
    /// - Delivery log entry creation and status updates
    /// </summary>
    internal static Func<NotificationDispatchTask, CancellationToken, Task> CreateHandler(IServiceProvider sp)
    {
        return async (task, ct) =>
        {
            using var scope = sp.CreateScope();
            var scopedProvider = scope.ServiceProvider;

            var commandHandler = scopedProvider.GetRequiredService<DispatchNotificationCommandHandler>();
            var logger = scopedProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("NotificationWorkerPool.Handler");

            logger.LogDebug(
                "Processing notification dispatch task: RecipientId={RecipientId}, TemplateId={TemplateId}.",
                task.RecipientId, task.TemplateId);

            // Convert the task into a command for the handler
            var command = new DispatchNotificationCommand(
                RecipientId: task.RecipientId,
                RecipientAddress: task.RecipientAddress,
                TemplateId: task.TemplateId,
                Variables: task.Variables);

            try
            {
                await commandHandler.HandleAsync(command, ct);

                logger.LogInformation(
                    "Notification dispatched successfully for recipient {RecipientId}, template {TemplateId}.",
                    task.RecipientId, task.TemplateId);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Missing required template variables"))
            {
                // Template variable validation failure — not retryable
                logger.LogWarning(ex,
                    "Notification dispatch failed for recipient {RecipientId}: {Message}. Not retryable.",
                    task.RecipientId, ex.Message);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
            {
                // Template or resource not found — not retryable
                logger.LogWarning(ex,
                    "Notification dispatch failed for recipient {RecipientId}: {Message}. Not retryable.",
                    task.RecipientId, ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Notification dispatch failed for recipient {RecipientId}, template {TemplateId}.",
                    task.RecipientId, task.TemplateId);
                throw; // Let the WorkerPool log the error and continue with next task
            }
        };
    }
}
