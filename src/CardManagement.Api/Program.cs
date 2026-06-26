using System.Reflection;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Infrastructure;
using CardManagement.Infrastructure.PlatformServices;
using CardManagement.Infrastructure.PlatformServices.DeveloperPortal;
using CardManagement.Infrastructure.Cards;
using CardManagement.Infrastructure.Configuration;
using CardManagement.Infrastructure.Hsm;
using CardManagement.Infrastructure.Iso8583;
using CardManagement.Infrastructure.Kafka;
using CardManagement.Infrastructure.Ledger;
using CardManagement.Infrastructure.Networking;
using CardManagement.Infrastructure.Persistence;
using CardManagement.Infrastructure.Persistence.Repositories;
using CardManagement.Infrastructure.Pipeline;
using CardManagement.Infrastructure.Resilience;
using CardManagement.Infrastructure.Routing;
using CardManagement.Infrastructure.Tcp;
using Confluent.Kafka;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration validation (fail-fast at startup)
// ---------------------------------------------------------------------------
var configValidator = new ConfigurationService(builder.Configuration,
    LoggerFactory.Create(b => b.AddConsole()).CreateLogger<ConfigurationService>());
var validationResult = configValidator.ValidateConfiguration();
if (!validationResult.IsSuccess)
{
    Console.Error.WriteLine("Configuration validation failed. Application cannot start.");
    Console.Error.WriteLine(validationResult.ErrorMessage);
    Environment.Exit(1);
}

// ---------------------------------------------------------------------------
// HSM Options binding from configuration
// ---------------------------------------------------------------------------
builder.Services.Configure<HsmOptions>(options =>
{
    options.Endpoint = builder.Configuration["HSM__ENDPOINT"] ?? string.Empty;
    if (ulong.TryParse(builder.Configuration["HSM__SLOT_ID"], out var slotId))
        options.SlotId = slotId;
    options.Pin = builder.Configuration["HSM__PIN"] ?? string.Empty;
    if (int.TryParse(builder.Configuration["HSM__AUTH_TIMEOUT_SECONDS"], out var authTimeout))
        options.AuthTimeoutSeconds = authTimeout;
    if (int.TryParse(builder.Configuration["HSM__OPERATION_TIMEOUT_SECONDS"], out var opTimeout))
        options.OperationTimeoutSeconds = opTimeout;
});

// ---------------------------------------------------------------------------
// EF Core DbContext with Npgsql provider
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration["DATABASE__CONNECTION_STRING"];
builder.Services.AddDbContext<CardManagementDbContext>(options =>
    options.UseNpgsql(connectionString));

// ---------------------------------------------------------------------------
// Port interface → Infrastructure implementation registrations
// ---------------------------------------------------------------------------

// Gateway and messaging
builder.Services.AddSingleton<IIso8583Gateway, Iso8583GatewayAdapter>();
builder.Services.AddSingleton<ITcpConnectionHandler, TcpConnectionHandler>();
builder.Services.AddSingleton<IOutboundConnectionPool, OutboundConnectionPool>();

// Routing and network management
builder.Services.AddSingleton<ICardProcessorRouter, CardProcessorRouter>();
builder.Services.AddSingleton<INetworkManagementHandler, NetworkManagementHandler>();

// Application services
builder.Services.AddScoped<IVirtualCardService, VirtualCardService>();
builder.Services.AddSingleton<IHsmService, HsmServiceAdapter>();
builder.Services.AddScoped<ILedgerService, LedgerService>();
builder.Services.AddScoped<ITransactionPipeline, TransactionPipeline>();

// Configuration validation
builder.Services.AddSingleton<IConfigurationValidator, ConfigurationService>();

// Repositories
builder.Services.AddScoped<ICardRepository, CardRepository>();
builder.Services.AddScoped<IAccountRepository, AccountRepository>();
builder.Services.AddScoped<ILedgerEntryRepository, LedgerEntryRepository>();
builder.Services.AddScoped<ITransactionRepository, TransactionRepository>();
builder.Services.AddScoped<IProcessorSessionRepository, ProcessorSessionRepository>();

// Unit of Work (scoped - one per request/operation)
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// ---------------------------------------------------------------------------
// Hosted background services
// ---------------------------------------------------------------------------
builder.Services.AddHostedService<TcpListenerService>();

// ---------------------------------------------------------------------------
// Swagger / OpenAPI
// ---------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Card Management API",
        Version = "v1",
        Description = """
            Payment gateway API for virtual card issuance, account balance queries, ISO 8583 transaction processing, 
            and platform services including reconciliation, webhooks, notifications, admin console, and developer portal.
            
            ## Authentication
            Platform services endpoints require an API key passed in the `X-Api-Key` header.
            
            ## Error Handling
            All errors return a consistent JSON structure with `error` (human-readable) and `code` (machine-readable) fields.
            
            ## Rate Limiting
            API key validation targets 5ms p99 latency via Redis cache.
            """,
        Contact = new OpenApiContact
        {
            Name = "Card Management Team",
            Email = "platform@cardmanagement.com"
        },
        License = new OpenApiLicense
        {
            Name = "Proprietary"
        }
    });

    // API Key security scheme
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-Api-Key",
        Description = "API key for developer portal authentication"
    });

    options.AddSecurityDefinition("IdempotencyKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-Idempotency-Key",
        Description = "Client-generated unique key for payment idempotency (required for POST /api/payments)"
    });

    // Tag descriptions for endpoint grouping
    options.TagActionsBy(api =>
    {
        if (api.RelativePath?.StartsWith("api/v1/reconciliation") == true)
            return new[] { "Reconciliation" };
        if (api.RelativePath?.StartsWith("api/v1/webhooks") == true)
            return new[] { "Webhooks" };
        if (api.RelativePath?.StartsWith("api/v1/notifications") == true)
            return new[] { "Notifications" };
        if (api.RelativePath?.StartsWith("api/v1/admin") == true)
            return new[] { "Admin Console" };
        if (api.RelativePath?.StartsWith("api/v1/developer") == true)
            return new[] { "Developer Portal" };
        if (api.RelativePath?.StartsWith("api/payments") == true)
            return new[] { "Payments" };
        if (api.RelativePath?.StartsWith("api/disputes") == true)
            return new[] { "Disputes" };
        if (api.RelativePath?.StartsWith("api/cards") == true)
            return new[] { "Cards" };
        if (api.RelativePath?.StartsWith("api/accounts") == true)
            return new[] { "Accounts" };
        return new[] { "Other" };
    });

    options.OrderActionsBy(api => api.RelativePath);

    // Include XML comments from all projects
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }

    // Include XML comments from domain and application projects
    var domainXml = Path.Combine(AppContext.BaseDirectory, "CardManagement.Domain.xml");
    if (File.Exists(domainXml))
        options.IncludeXmlComments(domainXml);

    var appXml = Path.Combine(AppContext.BaseDirectory, "CardManagement.Application.xml");
    if (File.Exists(appXml))
        options.IncludeXmlComments(appXml);
});

// ---------------------------------------------------------------------------
// MVC Controllers
// ---------------------------------------------------------------------------
builder.Services.AddControllers();

// ---------------------------------------------------------------------------
// Kafka Event Publishing
// ---------------------------------------------------------------------------
builder.Services.Configure<KafkaOptions>(options =>
{
    options.BootstrapServers = builder.Configuration["KAFKA__BOOTSTRAP_SERVERS"] ?? string.Empty;
    options.TopicPrefix = builder.Configuration["KAFKA__TOPIC_PREFIX"] ?? "cardmgmt.events";
    options.ClientId = builder.Configuration["KAFKA__CLIENT_ID"] ?? "card-management-api";
});

var kafkaBootstrapServers = builder.Configuration["KAFKA__BOOTSTRAP_SERVERS"];
if (!string.IsNullOrEmpty(kafkaBootstrapServers))
{
    builder.Services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
}
else
{
    builder.Services.AddSingleton<IEventPublisher, NullEventPublisher>();
}

// ---------------------------------------------------------------------------
// Shared Kafka Producer (singleton — used by payment service publishers)
// ---------------------------------------------------------------------------
var kafkaBootstrapFromSection = builder.Configuration["Kafka:BootstrapServers"]
    ?? kafkaBootstrapServers
    ?? "localhost:9092";

builder.Services.AddSingleton<IProducer<string, string>>(sp =>
{
    var config = new ProducerConfig
    {
        BootstrapServers = kafkaBootstrapFromSection,
        ClientId = builder.Configuration["KAFKA__CLIENT_ID"] ?? "card-management-api",
        Acks = Acks.Leader,
        EnableIdempotence = false,
        MessageTimeoutMs = 5000,
        RequestTimeoutMs = 3000
    };
    return new ProducerBuilder<string, string>(config).Build();
});

// ---------------------------------------------------------------------------
// Payment Integration Services (channels, orchestration, fraud, disputes, etc.)
// ---------------------------------------------------------------------------
builder.Services.AddPaymentServices(builder.Configuration);

// ---------------------------------------------------------------------------
// Platform Services (reconciliation, webhooks, notifications, admin console, developer portal)
// ---------------------------------------------------------------------------
builder.Services.AddPlatformServices(builder.Configuration);

// ---------------------------------------------------------------------------
// OpenTelemetry Metrics — RED (Rate, Errors, Duration) & USE (Utilization, Saturation, Errors)
// ---------------------------------------------------------------------------
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: "CardManagement.Api", serviceVersion: "1.0.0"))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()       // RED: request rate, duration, errors by endpoint
        .AddHttpClientInstrumentation()       // RED: outbound HTTP call metrics
        .AddRuntimeInstrumentation()          // USE: GC, thread pool, memory utilization
        .AddMeter("CardManagement.Payments")  // Custom payment channel metrics
        .AddMeter("CardManagement.Workers")   // USE: worker pool utilization/saturation
        .AddMeter("CardManagement.Redis")     // USE: Redis connection pool
        .AddPrometheusExporter());

// Register custom meters for RED/USE instrumentation
builder.Services.AddSingleton(sp => new Meter("CardManagement.Payments", "1.0.0"));
builder.Services.AddSingleton(sp => new Meter("CardManagement.Workers", "1.0.0"));
builder.Services.AddSingleton(sp => new Meter("CardManagement.Redis", "1.0.0"));

// ---------------------------------------------------------------------------
// Health Checks
// ---------------------------------------------------------------------------
var healthChecksBuilder = builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString!, name: "postgresql");

if (!string.IsNullOrEmpty(kafkaBootstrapServers))
{
    healthChecksBuilder.AddCheck<KafkaHealthCheck>("kafka");
}

healthChecksBuilder.AddCheck<PaymentChannelHealthCheck>("payment-channels");

var app = builder.Build();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            entries = report.Entries.ToDictionary(
                e => e.Key,
                e => new
                {
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    duration = e.Value.Duration.ToString()
                })
        };
        await context.Response.WriteAsJsonAsync(result);
    }
});

// ---------------------------------------------------------------------------
// Prometheus metrics endpoint — scraped by Prometheus at /metrics
// ---------------------------------------------------------------------------
app.MapPrometheusScrapingEndpoint();

// ---------------------------------------------------------------------------
// Swagger UI (available in all environments for this internal service)
// ---------------------------------------------------------------------------
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Card Management API v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "Card Management API — Swagger";
    options.DefaultModelsExpandDepth(1);
    options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
    options.EnableDeepLinking();
    options.DisplayRequestDuration();
});

// ---------------------------------------------------------------------------
// Platform Services Middleware Pipeline
// Order: API key validation → Request logging → Controllers
// ---------------------------------------------------------------------------
app.UseApiKeyValidation();
app.UseMiddleware<CardManagement.Infrastructure.PlatformServices.DeveloperPortal.RequestLoggingMiddleware>();

app.MapControllers();

app.Run();

// Make the auto-generated Program class accessible for integration testing with WebApplicationFactory
public partial class Program { }
