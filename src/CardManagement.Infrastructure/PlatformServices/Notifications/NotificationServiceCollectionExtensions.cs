using CardManagement.Application.PlatformServices.Notifications.Commands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.PlatformServices.Notifications;

/// <summary>
/// Extension methods for registering notification dispatch infrastructure in DI.
/// </summary>
public static class NotificationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="NotificationWorkerPool"/> and <see cref="NotificationKafkaConsumer"/>
    /// as hosted background services with configurable concurrency and Kafka options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNotificationDispatchInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure Kafka consumer options
        services.Configure<NotificationKafkaConsumerOptions>(
            configuration.GetSection(NotificationKafkaConsumerOptions.SectionName));

        // Register the DispatchNotificationCommandHandler as a scoped service
        services.AddScoped<DispatchNotificationCommandHandler>();

        // Register the generic worker pool for notification dispatch tasks
        var workerPoolOptions = new WorkerPoolOptions();
        var section = configuration.GetSection("NotificationWorkerPool");
        var concurrency = section.GetValue<int?>("Concurrency") ?? 8;
        var capacity = section.GetValue<int?>("Capacity") ?? 1000;
        workerPoolOptions.Concurrency = concurrency;
        workerPoolOptions.Capacity = capacity;

        services.AddWorkerPool<NotificationDispatchTask>(
            workerPoolOptions,
            NotificationWorkerPool.CreateHandler,
            poolName: "NotificationDispatch");

        // Register the NotificationWorkerPool facade
        services.AddSingleton<NotificationWorkerPool>(sp =>
        {
            var innerPool = sp.GetRequiredService<WorkerPool<NotificationDispatchTask>>();
            var logger = sp.GetRequiredService<ILogger<NotificationWorkerPool>>();
            return new NotificationWorkerPool(innerPool, logger);
        });

        // Register the Kafka consumer as a hosted service
        services.AddHostedService<NotificationKafkaConsumer>();

        return services;
    }
}
