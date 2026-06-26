using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Infrastructure.Audit;
using CardManagement.Infrastructure.Channels.CardSwitch;
using CardManagement.Infrastructure.Channels.Nibss;
using CardManagement.Infrastructure.Disputes;
using CardManagement.Infrastructure.Durable;
using CardManagement.Infrastructure.Fraud;
using CardManagement.Infrastructure.Idempotency;
using CardManagement.Infrastructure.Networking;
using CardManagement.Infrastructure.Orchestration;
using CardManagement.Infrastructure.Persistence.Repositories;
using CardManagement.Infrastructure.Resilience;
using CardManagement.Infrastructure.Saga;
using CardManagement.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CardManagement.Infrastructure;

/// <summary>
/// Extension methods for registering all payment integration services with the DI container.
/// </summary>
public static class PaymentServiceRegistration
{
    /// <summary>
    /// Registers all payment infrastructure services, channel adapters, resilience components,
    /// and configuration options with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">The application configuration root.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPaymentServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // -------------------------------------------------------------------
        // Options binding from configuration sections
        // -------------------------------------------------------------------
        services.Configure<NipOptions>(configuration.GetSection(NipOptions.SectionName));
        services.Configure<NqrOptions>(configuration.GetSection(NqrOptions.SectionName));
        services.Configure<EBillsPayOptions>(configuration.GetSection(EBillsPayOptions.SectionName));
        services.Configure<MCashOptions>(configuration.GetSection(MCashOptions.SectionName));
        services.Configure<DirectDebitOptions>(configuration.GetSection(DirectDebitOptions.SectionName));
        services.Configure<GapsOptions>(configuration.GetSection("Gaps"));
        services.Configure<InterswitchOptions>(configuration.GetSection(InterswitchOptions.SectionName));
        services.Configure<CardifyOptions>(configuration.GetSection(CardifyOptions.SectionName));
        services.Configure<CircuitBreakerOptions>(configuration.GetSection(CircuitBreakerOptions.SectionName));
        services.Configure<FraudRiskOptions>(configuration.GetSection(FraudRiskOptions.SectionName));
        services.Configure<DisputeOptions>(configuration.GetSection("Disputes"));
        services.Configure<DurableProcessorOptions>(configuration.GetSection(DurableProcessorOptions.SectionName));
        services.Configure<PciSecurityOptions>(configuration.GetSection(PciSecurityOptions.SectionName));
        services.Configure<SagaOptions>(configuration.GetSection("Saga"));

        // -------------------------------------------------------------------
        // Core payment services (scoped)
        // -------------------------------------------------------------------
        services.AddScoped<IPaymentOrchestrator, PaymentOrchestrator>();
        services.AddScoped<IIdempotencyGuard, IdempotencyGuard>();
        services.AddScoped<IAuditStore, AuditStore>();
        services.AddScoped<ISagaOrchestrator, SagaOrchestrator>();
        services.AddScoped<IFraudRiskEngine, FraudRiskEngine>();
        services.AddScoped<IDisputeService, DisputeService>();

        // -------------------------------------------------------------------
        // Circuit breaker registry (singleton — shared state across requests)
        // -------------------------------------------------------------------
        services.AddSingleton<ICircuitBreakerRegistry, CircuitBreakerRegistry>();

        // -------------------------------------------------------------------
        // Outbound connection pool (singleton — persistent connections)
        // -------------------------------------------------------------------
        services.AddSingleton<IOutboundConnectionPool, OutboundConnectionPool>();

        // -------------------------------------------------------------------
        // Durable processor (singleton hosted service)
        // -------------------------------------------------------------------
        services.AddSingleton<DurableProcessor>();
        services.AddSingleton<IDurableProcessor>(sp => sp.GetRequiredService<DurableProcessor>());
        services.AddHostedService(sp => sp.GetRequiredService<DurableProcessor>());

        // -------------------------------------------------------------------
        // PCI security boundary (scoped)
        // -------------------------------------------------------------------
        services.AddScoped<PciBoundary>();

        // -------------------------------------------------------------------
        // Channel adapters (scoped — one per request)
        // -------------------------------------------------------------------
        services.AddScoped<IChannelAdapter, NipAdapter>();
        services.AddScoped<IChannelAdapter, NqrAdapter>();
        services.AddScoped<IChannelAdapter, EBillsPayAdapter>();
        services.AddScoped<IChannelAdapter, MCashAdapter>();
        services.AddScoped<IChannelAdapter, DirectDebitAdapter>();
        services.AddScoped<IChannelAdapter, GapsAdapter>();
        services.AddScoped<IChannelAdapter, InterswitchAdapter>();
        services.AddScoped<IChannelAdapter, CardifyAdapter>();

        // -------------------------------------------------------------------
        // Repositories (scoped)
        // -------------------------------------------------------------------
        services.AddScoped<IDisputeRepository, DisputeRepository>();
        services.AddScoped<IPaymentRequestRepository, PaymentRequestRepository>();
        services.AddScoped<ISagaStateRepository, SagaStateRepository>();

        // -------------------------------------------------------------------
        // Event publishers (scoped)
        // -------------------------------------------------------------------
        services.AddScoped<IDisputeEventPublisher, KafkaDisputeEventPublisher>();

        return services;
    }
}
