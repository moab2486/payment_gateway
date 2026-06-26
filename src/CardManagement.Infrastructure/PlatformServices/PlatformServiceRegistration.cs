using CardManagement.Application.PlatformServices.AdminConsole.Ports;
using CardManagement.Application.PlatformServices.AdminConsole.Services;
using CardManagement.Application.PlatformServices.DeveloperPortal;
using CardManagement.Application.PlatformServices.DeveloperPortal.Commands;
using CardManagement.Application.PlatformServices.DeveloperPortal.Queries;
using CardManagement.Application.PlatformServices.Notifications.Commands;
using CardManagement.Application.PlatformServices.Notifications.Ports;
using CardManagement.Application.PlatformServices.Notifications.Queries;
using CardManagement.Application.PlatformServices.Reconciliation.DTOs;
using CardManagement.Application.PlatformServices.Reconciliation.Handlers;
using CardManagement.Application.PlatformServices.Reconciliation.Ports;
using CardManagement.Application.PlatformServices.Webhooks.Handlers;
using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Infrastructure.PlatformServices.AdminConsole;
using CardManagement.Infrastructure.PlatformServices.AdminConsole.Persistence;
using CardManagement.Infrastructure.PlatformServices.DeveloperPortal;
using CardManagement.Infrastructure.PlatformServices.DeveloperPortal.Persistence;
using CardManagement.Infrastructure.PlatformServices.Notifications;
using CardManagement.Infrastructure.PlatformServices.Notifications.Persistence;
using CardManagement.Infrastructure.PlatformServices.Reconciliation;
using CardManagement.Infrastructure.PlatformServices.Webhooks;
using CardManagement.Infrastructure.PlatformServices.Webhooks.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CardManagement.Infrastructure.PlatformServices;

/// <summary>
/// Extension methods for registering platform services with the DI container.
/// </summary>
public static class PlatformServiceRegistration
{
    /// <summary>
    /// Registers all platform services: reconciliation, webhooks, notifications,
    /// admin console, and developer portal infrastructure and handlers.
    /// </summary>
    public static IServiceCollection AddPlatformServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Redis connection multiplexer (shared across services)
        services.AddRedisConnection(configuration);

        // Reconciliation
        services.AddReconciliationServices(configuration);

        // Webhooks
        services.AddWebhookServices(configuration);

        // Notifications
        services.AddNotificationServices(configuration);

        // Admin Console
        services.AddAdminConsoleServices(configuration);

        // Developer Portal
        services.AddDeveloperPortalServices(configuration);

        return services;
    }

    private static void AddReconciliationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options
        services.Configure<AdjustmentRuleOptions>(
            configuration.GetSection("Reconciliation:AdjustmentRules"));

        // Repository
        services.AddScoped<IReconciliationRepository, ReconciliationRepository>();

        // Infrastructure services
        services.AddScoped<ISettlementFileParser, SettlementFileParserFactory>();
        services.AddScoped<IReconciliationEngine, ReconciliationEngine>();
        services.AddScoped<IAdjustmentRuleEngine, AdjustmentRuleEngine>();
        services.AddSingleton<IReconciliationEventPublisher, KafkaReconciliationEventPublisher>();

        // Worker pool infrastructure
        services.AddSingleton<WorkerPool<FileParseTask>>(sp =>
        {
            var concurrency = configuration.GetValue("ReconciliationWorkerPool:Concurrency", 4);
            var capacity = configuration.GetValue("ReconciliationWorkerPool:Capacity", 100);
            var options = new WorkerPoolOptions { Concurrency = concurrency, Capacity = capacity };
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<WorkerPool<FileParseTask>>();
            var handler = ReconciliationWorkerPool.CreateHandler(sp);

            return new WorkerPool<FileParseTask>(
                options,
                handler,
                logger,
                "ReconciliationWorkerPool");
        });
        services.AddHostedService(sp => sp.GetRequiredService<WorkerPool<FileParseTask>>());
        services.AddSingleton<IReconciliationWorkerPool, ReconciliationWorkerPool>();

        // Command and query handlers
        services.AddScoped<ImportSettlementFileCommandHandler>();
        services.AddScoped<GetBatchQueryHandler>();
        services.AddScoped<ListBatchesQueryHandler>();
        services.AddScoped<ListExceptionsQueryHandler>();
        services.AddScoped<CreateManualAdjustmentCommandHandler>();
        services.AddScoped<ListAdjustmentsQueryHandler>();
    }

    private static void AddWebhookServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Repositories
        services.AddScoped<IWebhookSubscriptionRepository, WebhookSubscriptionRepository>();
        services.AddScoped<IWebhookDeliveryRepository, WebhookDeliveryRepository>();

        // Infrastructure services
        services.AddHttpClient<IUrlVerificationService, UrlVerificationService>();
        services.AddSingleton<IHmacSigner, HmacSigner>();
        services.AddSingleton<IWebhookCircuitBreakerRegistry, WebhookCircuitBreakerRegistry>();
        services.AddHttpClient<IWebhookDeliveryEngine, WebhookDeliveryEngine>();

        // Command and query handlers
        services.AddScoped<CreateSubscriptionCommandHandler>();
        services.AddScoped<UpdateSubscriptionCommandHandler>();
        services.AddScoped<DeactivateSubscriptionCommandHandler>();
        services.AddScoped<GetDeliveryHistoryQueryHandler>();
        services.AddScoped<GetDlqItemsQueryHandler>();
        services.AddScoped<ReplayFromDlqCommandHandler>();
        services.AddScoped<RetryDeliveryCommandHandler>();
        services.AddScoped<DeliverWebhookCommandHandler>();

        // Worker pool and Kafka consumer infrastructure
        services.AddWebhookDeliveryInfrastructure(configuration);
    }

    private static void AddAdminConsoleServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Repositories
        services.AddScoped<IAdminCommandRepository, AdminCommandRepository>();
        services.AddScoped<IAdminRoleRepository, AdminRoleRepository>();

        // RBAC service (application layer service backed by database-stored roles)
        services.AddScoped<IRbacService, RbacService>();

        // Read model query service and staleness tracker
        services.AddSingleton<ReadModelStalenessTracker>();
        services.AddScoped<IAdminReadModelQuery, AdminReadModelQueryService>();

        // Maker-Checker workflow service
        services.AddScoped<IMakerCheckerWorkflow>(sp =>
        {
            var commandRepo = sp.GetRequiredService<IAdminCommandRepository>();
            var auditStore = sp.GetRequiredService<Application.Ports.IAuditStore>();
            var eventPublisher = sp.GetRequiredService<Application.Ports.IEventPublisher>();
            var rbacService = sp.GetRequiredService<IRbacService>();
            var adminEventPublisher = sp.GetRequiredService<IAdminConsoleEventPublisher>();
            var expiryHours = configuration.GetValue("AdminConsole:CommandExpiryHours", 24);
            var expiryPeriod = TimeSpan.FromHours(expiryHours);

            return new MakerCheckerWorkflowService(
                commandRepo, auditStore, eventPublisher, rbacService, expiryPeriod, adminEventPublisher);
        });

        // Kafka event publisher for admin console events
        services.AddSingleton<IAdminConsoleEventPublisher, KafkaAdminConsoleEventPublisher>();

        // Read model projectors (individual projectors are scoped for DbContext access)
        services.AddScoped<TransactionSummaryProjector>();
        services.AddScoped<ReconciliationStatusProjector>();
        services.AddScoped<DisputeMetricsProjector>();
        services.AddScoped<ChannelHealthProjector>();

        // Composite projector that delegates to all individual projectors
        services.AddScoped<IAdminReadModelProjector, AdminReadModelProjector>();

        // Admin read model Kafka consumer (background service)
        services.Configure<AdminReadModelKafkaConsumerOptions>(
            configuration.GetSection(AdminReadModelKafkaConsumerOptions.SectionName));
        services.AddHostedService<AdminReadModelKafkaConsumer>();

        // Command expiry background service
        services.AddHostedService(sp =>
        {
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            var logger = sp.GetRequiredService<ILogger<CommandExpiryBackgroundService>>();
            var checkIntervalMinutes = configuration.GetValue("AdminConsole:ExpiryCheckIntervalMinutes", 5);
            var expiryHours = configuration.GetValue("AdminConsole:CommandExpiryHours", 24);

            return new CommandExpiryBackgroundService(
                scopeFactory,
                logger,
                TimeSpan.FromMinutes(checkIntervalMinutes),
                TimeSpan.FromHours(expiryHours));
        });
    }

    private static void AddDeveloperPortalServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options
        services.Configure<ApiKeyValidationOptions>(
            configuration.GetSection(ApiKeyValidationOptions.SectionName));
        services.Configure<ApiKeyCacheOptions>(
            configuration.GetSection(ApiKeyCacheOptions.SectionName));

        // Repositories
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<IRequestLogRepository, RequestLogRepository>();

        // Redis-backed API key cache service
        services.AddSingleton<IApiKeyCacheService, RedisApiKeyCacheService>();

        // API key service (core application logic)
        services.AddScoped<IApiKeyService, ApiKeyService>();

        // Sandbox environment
        services.AddScoped<ISandboxEnvironment, SandboxEnvironment>();

        // Command and query handlers
        services.AddScoped<CreateApiKeyCommandHandler>();
        services.AddScoped<RotateApiKeyCommandHandler>();
        services.AddScoped<RevokeApiKeyCommandHandler>();
        services.AddScoped<ValidateApiKeyQueryHandler>();
    }

    private static void AddRedisConnection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnectionString = configuration["DeveloperPortal:ApiKeyCache:RedisConnectionString"]
            ?? "localhost:6379";

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = ConfigurationOptions.Parse(redisConnectionString);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });
    }

    private static void AddNotificationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options
        services.Configure<EmailChannelOptions>(
            configuration.GetSection("Notifications:Email"));
        services.Configure<SmsChannelOptions>(
            configuration.GetSection("Notifications:Sms"));
        services.Configure<WhatsAppChannelOptions>(
            configuration.GetSection("Notifications:WhatsApp"));
        services.Configure<NotificationCircuitBreakerOptions>(
            configuration.GetSection("Notifications:CircuitBreaker"));

        // Repositories
        services.AddScoped<INotificationTemplateRepository, NotificationTemplateRepository>();
        services.AddScoped<INotificationPreferenceRepository, NotificationPreferenceRepository>();
        services.AddScoped<IDeliveryLogRepository, DeliveryLogRepository>();

        // Template renderer
        services.AddSingleton<ITemplateRenderer, TemplateRenderer>();

        // Circuit breaker registry for notification channels
        services.AddSingleton<INotificationCircuitBreakerRegistry, NotificationCircuitBreakerRegistry>();

        // Channel adapters (registered as INotificationChannelAdapter for multi-channel resolution)
        // Email uses SMTP (no HttpClient needed)
        services.AddScoped<INotificationChannelAdapter, EmailChannelAdapter>();
        // SMS and WhatsApp use HTTP — register typed HttpClients then map to interface
        services.AddHttpClient<SmsChannelAdapter>();
        services.AddScoped<INotificationChannelAdapter>(sp => sp.GetRequiredService<SmsChannelAdapter>());
        services.AddHttpClient<WhatsAppChannelAdapter>();
        services.AddScoped<INotificationChannelAdapter>(sp => sp.GetRequiredService<WhatsAppChannelAdapter>());

        // Query handlers
        services.AddScoped<CreateTemplateCommandHandler>();
        services.AddScoped<UpdateTemplateCommandHandler>();
        services.AddScoped<GetDeliveryLogQueryHandler>();
        services.AddScoped<GetDeliveryStatisticsQueryHandler>();
        services.AddScoped<GetPreferencesQueryHandler>();

        // Worker pool and Kafka consumer infrastructure
        services.AddNotificationDispatchInfrastructure(configuration);
    }
}
