using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.PlatformServices;

/// <summary>
/// Extension methods for registering worker pool instances in DI.
/// </summary>
public static class WorkerPoolServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="WorkerPool{TTask}"/> as a hosted background service.
    /// Configuration is bound from the specified configuration section.
    /// </summary>
    /// <typeparam name="TTask">The task item type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="configSectionName">The configuration section name (e.g., "ReconciliationWorkerPool").</param>
    /// <param name="handler">The async delegate that processes each task.</param>
    /// <param name="poolName">Optional human-readable pool name for logging.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddWorkerPool<TTask>(
        this IServiceCollection services,
        IConfiguration configuration,
        string configSectionName,
        Func<IServiceProvider, Func<TTask, CancellationToken, Task>> handlerFactory,
        string? poolName = null)
    {
        services.AddSingleton(sp =>
        {
            var options = new WorkerPoolOptions();
            configuration.GetSection(configSectionName).Bind(options);

            var handler = handlerFactory(sp);
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger($"WorkerPool.{poolName ?? configSectionName}");

            return new WorkerPool<TTask>(options, handler, logger, poolName ?? configSectionName);
        });

        services.AddHostedService(sp => sp.GetRequiredService<WorkerPool<TTask>>());

        return services;
    }

    /// <summary>
    /// Registers a <see cref="WorkerPool{TTask}"/> with explicit options (useful for testing
    /// or scenarios where configuration comes from a different source).
    /// </summary>
    public static IServiceCollection AddWorkerPool<TTask>(
        this IServiceCollection services,
        WorkerPoolOptions options,
        Func<IServiceProvider, Func<TTask, CancellationToken, Task>> handlerFactory,
        string? poolName = null)
    {
        services.AddSingleton(sp =>
        {
            var handler = handlerFactory(sp);
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger($"WorkerPool.{poolName ?? typeof(TTask).Name}");

            return new WorkerPool<TTask>(options, handler, logger, poolName);
        });

        services.AddHostedService(sp => sp.GetRequiredService<WorkerPool<TTask>>());

        return services;
    }
}
