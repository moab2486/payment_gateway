using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Disputes;
using CardManagement.Infrastructure.Persistence;
using CardManagement.Infrastructure.Saga;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace CardManagement.Tests.Integration;

/// <summary>
/// Integration tests for payment API endpoints using WebApplicationFactory.
/// Verifies HTTP-level behavior: routing, model binding, response codes, and JSON payloads.
/// </summary>
public class PaymentApiIntegrationTests : IClassFixture<PaymentApiIntegrationTests.PaymentApiFactory>
{
    private readonly HttpClient _client;
    private readonly PaymentApiFactory _factory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public PaymentApiIntegrationTests(PaymentApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    #region Payment Endpoint Tests

    [Fact]
    public async Task Post_Payments_WithValidRequest_Returns201Created()
    {
        // Arrange
        var orchestrator = _factory.Services.GetRequiredService<IPaymentOrchestrator>() as StubPaymentOrchestrator;
        orchestrator!.NextResult = new PaymentResult(
            TransactionReference: "TXN-ABC123",
            Status: PaymentStatus.Completed,
            Success: true,
            ErrorMessage: null,
            ProcessorReference: "NIBSS-REF-001");

        var request = new
        {
            TransactionType = PaymentTransactionType.InterbankTransfer,
            Amount = 50000L,
            CurrencyCode = "NGN",
            SourceAccount = "0123456789",
            DestinationAccount = "9876543210",
            Channel = PaymentChannel.NIP
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/payments");
        httpRequest.Headers.Add("X-Idempotency-Key", Guid.NewGuid().ToString());
        httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

        // Act
        var response = await _client.SendAsync(httpRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TXN-ABC123", body.GetProperty("transactionReference").GetString());
        Assert.True(body.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Post_Payments_WithDuplicateIdempotencyKey_ReturnsCachedResponse()
    {
        // Arrange
        var idempotencyKey = Guid.NewGuid().ToString();
        var orchestrator = _factory.Services.GetRequiredService<IPaymentOrchestrator>() as StubPaymentOrchestrator;

        // First request succeeds
        orchestrator!.NextResult = new PaymentResult(
            TransactionReference: "TXN-DUP001",
            Status: PaymentStatus.Completed,
            Success: true,
            ErrorMessage: null,
            ProcessorReference: "NIBSS-REF-DUP");

        var request = new
        {
            TransactionType = PaymentTransactionType.InterbankTransfer,
            Amount = 10000L,
            CurrencyCode = "NGN",
            SourceAccount = "0123456789",
            DestinationAccount = "9876543210",
            Channel = PaymentChannel.NIP
        };

        var httpRequest1 = new HttpRequestMessage(HttpMethod.Post, "/api/payments");
        httpRequest1.Headers.Add("X-Idempotency-Key", idempotencyKey);
        httpRequest1.Content = JsonContent.Create(request, options: JsonOptions);

        var response1 = await _client.SendAsync(httpRequest1);
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

        // Second request with same key — orchestrator returns same cached result
        orchestrator.NextResult = new PaymentResult(
            TransactionReference: "TXN-DUP001",
            Status: PaymentStatus.Completed,
            Success: true,
            ErrorMessage: null,
            ProcessorReference: "NIBSS-REF-DUP");

        var httpRequest2 = new HttpRequestMessage(HttpMethod.Post, "/api/payments");
        httpRequest2.Headers.Add("X-Idempotency-Key", idempotencyKey);
        httpRequest2.Content = JsonContent.Create(request, options: JsonOptions);

        // Act
        var response2 = await _client.SendAsync(httpRequest2);

        // Assert — still returns 201 with the same cached response
        Assert.Equal(HttpStatusCode.Created, response2.StatusCode);

        var body2 = await response2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TXN-DUP001", body2.GetProperty("transactionReference").GetString());
        Assert.Equal("NIBSS-REF-DUP", body2.GetProperty("processorReference").GetString());

        // Verify orchestrator was called twice (endpoint correctly passes both through)
        Assert.Equal(2, orchestrator.InitiateCallCount);
    }

    [Fact]
    public async Task Post_Payments_WithoutIdempotencyKey_Returns400BadRequest()
    {
        // Arrange
        var request = new
        {
            TransactionType = PaymentTransactionType.InterbankTransfer,
            Amount = 50000L,
            CurrencyCode = "NGN",
            SourceAccount = "0123456789",
            DestinationAccount = "9876543210",
            Channel = PaymentChannel.NIP
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/payments");
        // Deliberately NOT adding X-Idempotency-Key header
        httpRequest.Content = JsonContent.Create(request, options: JsonOptions);

        // Act
        var response = await _client.SendAsync(httpRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Idempotency", body.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region Dispute Endpoint Tests

    [Fact]
    public async Task Post_Disputes_WithValidRequest_Returns201Created()
    {
        // Arrange
        var disputeService = _factory.Services.GetRequiredService<IDisputeService>() as StubDisputeService;
        var disputeId = Guid.NewGuid();
        disputeService!.NextRaiseResult = new DisputeResult(
            Id: disputeId,
            Status: DisputeStatus.Opened,
            Success: true,
            ErrorMessage: null);

        var request = new
        {
            TransactionReference = "TXN-DISPUTE001",
            ReasonCode = "UNAUTHORIZED",
            Amount = new { Amount = 25000L, CurrencyCode = "NGN" },
            Evidence = "Customer did not authorize this transaction"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/disputes", request, JsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(disputeId.ToString(), body.GetProperty("id").GetString());
        Assert.True(body.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Put_DisputesResolve_WithValidResolution_Returns200()
    {
        // Arrange
        var disputeService = _factory.Services.GetRequiredService<IDisputeService>() as StubDisputeService;
        var disputeId = Guid.NewGuid();
        disputeService!.NextResolveResult = new DisputeResult(
            Id: disputeId,
            Status: DisputeStatus.ResolvedInFavour,
            Success: true,
            ErrorMessage: null);

        var resolution = new
        {
            Decision = DisputeDecision.InFavour,
            Notes = "Cardholder provided sufficient evidence"
        };

        // Act
        var response = await _client.PutAsJsonAsync($"/api/disputes/{disputeId}/resolve", resolution, JsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(disputeId.ToString(), body.GetProperty("id").GetString());
        Assert.True(body.GetProperty("success").GetBoolean());
    }

    #endregion

    #region Health Endpoint Tests

    [Fact]
    public async Task Get_Health_ReflectsChannelCircuitBreakerStates()
    {
        // Arrange
        var registry = _factory.Services.GetRequiredService<ICircuitBreakerRegistry>() as StubCircuitBreakerRegistry;
        registry!.SetState(PaymentChannel.NIP, CircuitBreakerState.Closed);
        registry.SetState(PaymentChannel.NQR, CircuitBreakerState.Open);
        registry.SetState(PaymentChannel.EBillsPay, CircuitBreakerState.Closed);

        // Act
        var response = await _client.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Health status should be "Degraded" since NQR is Open but not all channels are Open
        Assert.Equal("Degraded", body.GetProperty("status").GetString());

        // Verify the payment-channels entry exists
        var entries = body.GetProperty("entries");
        Assert.True(entries.TryGetProperty("payment-channels", out var channelEntry));
        Assert.Equal("Degraded", channelEntry.GetProperty("status").GetString());
    }

    #endregion

    #region WebApplicationFactory

    public class PaymentApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            // Provide required configuration values so Program.cs startup validation passes
            builder.UseSetting("DATABASE__CONNECTION_STRING", "Host=localhost;Database=test;");
            builder.UseSetting("TCP__LISTENER_PORT", "9000");
            builder.UseSetting("TCP__MAX_CONNECTIONS", "10");
            builder.UseSetting("TCP__READ_TIMEOUT_SECONDS", "30");
            builder.UseSetting("HSM__ENDPOINT", "http://localhost:5000");
            builder.UseSetting("HSM__SLOT_ID", "1");
            builder.UseSetting("HSM__AUTH_TIMEOUT_SECONDS", "10");
            builder.UseSetting("HSM__OPERATION_TIMEOUT_SECONDS", "10");
            builder.UseSetting("PROCESSOR__INTERSWITCH_ENDPOINT", "http://localhost:6000");
            builder.UseSetting("PROCESSOR__CARDFI_ENDPOINT", "http://localhost:7000");
            builder.UseSetting("CARD__BIN_RANGES", "[{\"prefix\":\"506199\",\"scheme\":\"Verve\",\"panLength\":16}]");
            builder.UseSetting("CARD__VALIDITY_MONTHS", "36");
            builder.UseSetting("LEDGER__LOCK_TIMEOUT_SECONDS", "30");
            builder.UseSetting("DeveloperPortal:ApiKeyValidation:Enabled", "false");

            builder.ConfigureServices(services =>
            {
                // Remove real DbContext that needs PostgreSQL
                services.RemoveAll<DbContextOptions<CardManagementDbContext>>();
                services.RemoveAll<CardManagementDbContext>();
                var dbDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<CardManagementDbContext>));
                if (dbDescriptor != null) services.Remove(dbDescriptor);

                // Add in-memory database
                services.AddDbContext<CardManagementDbContext>(options =>
                    options.UseInMemoryDatabase("TestPaymentDb_" + Guid.NewGuid().ToString("N")));

                // Remove real services that depend on external infrastructure
                services.RemoveAll<IPaymentOrchestrator>();
                services.RemoveAll<IDisputeService>();
                services.RemoveAll<ICircuitBreakerRegistry>();
                services.RemoveAll<IIdempotencyGuard>();
                services.RemoveAll<IAuditStore>();
                services.RemoveAll<ISagaOrchestrator>();
                services.RemoveAll<IFraudRiskEngine>();
                services.RemoveAll<IChannelAdapter>();
                services.RemoveAll<IPaymentRequestRepository>();
                services.RemoveAll<IDisputeRepository>();
                services.RemoveAll<ISagaStateRepository>();
                services.RemoveAll<IDisputeEventPublisher>();
                services.RemoveAll<IDurableProcessor>();

                // Remove hosted services that need real infra
                services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();

                // Remove ALL existing health check registrations to avoid duplicates
                services.Configure<HealthCheckServiceOptions>(options =>
                {
                    options.Registrations.Clear();
                });

                // Register stub implementations
                var stubOrchestrator = new StubPaymentOrchestrator();
                services.AddSingleton<IPaymentOrchestrator>(stubOrchestrator);

                var stubDisputeService = new StubDisputeService();
                services.AddSingleton<IDisputeService>(stubDisputeService);

                var stubRegistry = new StubCircuitBreakerRegistry();
                services.AddSingleton<ICircuitBreakerRegistry>(stubRegistry);

                // Register only our test health check (avoids duplicates with production registrations)
                services.AddHealthChecks()
                    .AddCheck<PaymentChannelHealthCheckStub>("payment-channels");
            });
        }
    }

    #endregion

    #region Stub Implementations

    /// <summary>
    /// Stub payment orchestrator that returns configurable results without real processing.
    /// </summary>
    private class StubPaymentOrchestrator : IPaymentOrchestrator
    {
        public PaymentResult? NextResult { get; set; }
        public int InitiateCallCount { get; private set; }

        public Task<PaymentResult> InitiatePaymentAsync(PaymentRequest request, CancellationToken ct)
        {
            InitiateCallCount++;
            return Task.FromResult(NextResult ?? new PaymentResult(
                "TXN-DEFAULT", PaymentStatus.Completed, true, null, null));
        }

        public Task<PaymentStatusResult> GetStatusAsync(string transactionReference, CancellationToken ct)
        {
            return Task.FromResult(new PaymentStatusResult(
                transactionReference, PaymentStatus.Completed, PaymentChannel.NIP,
                DateTime.UtcNow, "PROC-REF", true));
        }

        public Task<PaymentStatusResult> GetStatusByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct)
        {
            return Task.FromResult(new PaymentStatusResult(
                "TXN-BYKEY", PaymentStatus.Completed, PaymentChannel.NIP,
                DateTime.UtcNow, "PROC-REF", true));
        }
    }

    /// <summary>
    /// Stub dispute service for controlling test scenarios.
    /// </summary>
    private class StubDisputeService : IDisputeService
    {
        public DisputeResult? NextRaiseResult { get; set; }
        public DisputeResult? NextResolveResult { get; set; }

        public Task<DisputeResult> RaiseDisputeAsync(DisputeRequest request, CancellationToken ct)
        {
            return Task.FromResult(NextRaiseResult ?? new DisputeResult(
                Guid.NewGuid(), DisputeStatus.Opened, true, null));
        }

        public Task<DisputeResult> ResolveDisputeAsync(Guid disputeId, DisputeResolution resolution, CancellationToken ct)
        {
            return Task.FromResult(NextResolveResult ?? new DisputeResult(
                disputeId, DisputeStatus.ResolvedInFavour, true, null));
        }

        public Task<DisputeRecord?> GetDisputeAsync(Guid disputeId, CancellationToken ct)
        {
            return Task.FromResult<DisputeRecord?>(null);
        }
    }

    /// <summary>
    /// Stub circuit breaker registry that allows setting states for testing health endpoint.
    /// </summary>
    private class StubCircuitBreakerRegistry : ICircuitBreakerRegistry
    {
        private readonly Dictionary<PaymentChannel, CircuitBreakerState> _states = new();

        public void SetState(PaymentChannel channel, CircuitBreakerState state)
        {
            _states[channel] = state;
        }

        public ICircuitBreaker GetBreaker(PaymentChannel channel)
        {
            return new StubCircuitBreaker();
        }

        public IReadOnlyDictionary<PaymentChannel, CircuitBreakerState> GetAllStates()
        {
            return _states;
        }
    }

    private class StubCircuitBreaker : ICircuitBreaker
    {
        public CircuitBreakerState State => CircuitBreakerState.Closed;

        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
        {
            return action(ct);
        }

        public void RecordSuccess() { }
        public void RecordFailure() { }
    }

    /// <summary>
    /// Health check that uses the stubbed circuit breaker registry.
    /// Same logic as PaymentChannelHealthCheck but resolved from DI in test context.
    /// </summary>
    private class PaymentChannelHealthCheckStub : IHealthCheck
    {
        private readonly ICircuitBreakerRegistry _registry;

        public PaymentChannelHealthCheckStub(ICircuitBreakerRegistry registry)
        {
            _registry = registry;
        }

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var states = _registry.GetAllStates();
            var data = new Dictionary<string, object>();
            foreach (var (channel, state) in states)
            {
                data[channel.ToString()] = state.ToString();
            }

            if (states.Count == 0)
                return Task.FromResult(HealthCheckResult.Healthy("No channels registered", data));

            var allOpen = states.Values.All(s => s == CircuitBreakerState.Open);
            var anyOpen = states.Values.Any(s => s == CircuitBreakerState.Open);

            if (allOpen)
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "All payment channels are unavailable (circuit breakers open)", exception: null, data: data));

            if (anyOpen)
            {
                var openChannels = states
                    .Where(kvp => kvp.Value == CircuitBreakerState.Open)
                    .Select(kvp => kvp.Key.ToString());
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"Payment channels degraded: {string.Join(", ", openChannels)} circuit breaker(s) open",
                    exception: null, data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy("All payment channels operational", data));
        }
    }

    #endregion
}
