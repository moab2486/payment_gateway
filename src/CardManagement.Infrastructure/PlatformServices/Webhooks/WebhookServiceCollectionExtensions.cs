using CardManagement.Application.PlatformServices.Webhooks.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.PlatformServices.Webhooks;

/// <summary>
/// Extension methods for registering webhook delivery infrastructure in DI.
/// </summary>
public static class WebhookServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="WebhookDeliveryWorkerPool"/> and <see cref="WebhookEventKafkaConsumer"/>
    /// as hosted background services with configurable concurrency and Kafka options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddWebhookDeliveryInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure options
        services.Configure<WebhookDeliveryOptions>(
            configuration.GetSection(WebhookDeliveryOptions.SectionName));
        services.Configure<WebhookKafkaConsumerOptions>(
            configuration.GetSection(WebhookKafkaConsumerOptions.SectionName));

        // Register the generic worker pool for webhook delivery tasks
        var workerPoolOptions = new WorkerPoolOptions();
        var deliverySection = configuration.GetSection(WebhookDeliveryOptions.SectionName);
        var concurrency = deliverySection.GetValue<int?>("Concurrency") ?? 16;
        var capacity = deliverySection.GetValue<int?>("Capacity") ?? 1000;
        workerPoolOptions.Concurrency = concurrency;
        workerPoolOptions.Capacity = capacity;

        services.AddWorkerPool<WebhookDeliveryTask>(
            workerPoolOptions,
            WebhookDeliveryWorkerPool.CreateHandler,
            poolName: "WebhookDelivery");

        // Register the WebhookDeliveryWorkerPool facade
        services.AddSingleton<WebhookDeliveryWorkerPool>(sp =>
        {
            var innerPool = sp.GetRequiredService<WorkerPool<WebhookDeliveryTask>>();
            var logger = sp.GetRequiredService<ILogger<WebhookDeliveryWorkerPool>>();
            return new WebhookDeliveryWorkerPool(innerPool, logger);
        });

        // Register the Kafka consumer as a hosted service
        services.AddHostedService<WebhookEventKafkaConsumer>();

        return services;
    }
}
