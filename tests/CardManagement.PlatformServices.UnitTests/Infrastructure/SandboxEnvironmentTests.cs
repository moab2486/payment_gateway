using System.Text.Json;
using CardManagement.Application.PlatformServices.DeveloperPortal;
using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Domain.PlatformServices.Webhooks;
using CardManagement.Infrastructure.PlatformServices.DeveloperPortal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Infrastructure;

public class SandboxEnvironmentTests
{
    private readonly SandboxEnvironment _sandbox;
    private readonly FakeWebhookDeliveryEngine _webhookEngine;
    private readonly FakeWebhookSubscriptionRepository _subscriptionRepo;
    private readonly ILogger<SandboxEnvironment> _logger;

    public SandboxEnvironmentTests()
    {
        _logger = NullLoggerFactory.Instance.CreateLogger<SandboxEnvironment>();
        _webhookEngine = new FakeWebhookDeliveryEngine();
        _subscriptionRepo = new FakeWebhookSubscriptionRepository();
        _sandbox = new SandboxEnvironment(_webhookEngine, _subscriptionRepo, _logger);
    }

    #region Test Scenarios

    [Fact]
    public async Task ProcessRequest_SuccessfulPayment_WithTestCardNumber()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var request = CreatePaymentRequest(developerId, "4000000000000000", 1000, "NGN");

        // Act
        var response = await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(200, response.StatusCode);
        Assert.NotNull(response.ResponseBody);

        var body = JsonDocument.Parse(response.ResponseBody);
        Assert.Equal("succeeded", body.RootElement.GetProperty("status").GetString());
        Assert.NotNull(body.RootElement.GetProperty("authorizationCode").GetString());
        Assert.NotNull(body.RootElement.GetProperty("processorReference").GetString());
    }

    [Fact]
    public async Task ProcessRequest_DeclinedPayment_WithTestCardNumber()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var request = CreatePaymentRequest(developerId, "4000000000000002", 1000, "NGN");

        // Act
        var response = await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(200, response.StatusCode);
        Assert.NotNull(response.ResponseBody);

        var body = JsonDocument.Parse(response.ResponseBody);
        Assert.Equal("declined", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("card_declined", body.RootElement.GetProperty("declineCode").GetString());
    }

    [Fact]
    public async Task ProcessRequest_Timeout_WithTestCardNumber()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var request = CreatePaymentRequest(developerId, "4000000000000010", 1000, "NGN");

        // Act
        var response = await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(504, response.StatusCode);
        Assert.NotNull(response.ResponseBody);

        var body = JsonDocument.Parse(response.ResponseBody);
        Assert.Equal("failed", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("processing_timeout", body.RootElement.GetProperty("declineCode").GetString());
    }

    [Fact]
    public async Task ProcessRequest_InsufficientFunds_WithTestCardNumber()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var request = CreatePaymentRequest(developerId, "4000000000000019", 1000, "NGN");

        // Act
        var response = await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(200, response.StatusCode);
        Assert.NotNull(response.ResponseBody);

        var body = JsonDocument.Parse(response.ResponseBody);
        Assert.Equal("declined", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("insufficient_funds", body.RootElement.GetProperty("declineCode").GetString());
    }

    [Fact]
    public async Task ProcessRequest_DeclinedPayment_WithAmountTrigger()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var request = CreatePaymentRequest(developerId, "5500000000000001", 99999, "NGN");

        // Act
        var response = await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(200, response.StatusCode);
        Assert.NotNull(response.ResponseBody);

        var body = JsonDocument.Parse(response.ResponseBody);
        Assert.Equal("declined", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ProcessRequest_Timeout_WithAmountTrigger()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var request = CreatePaymentRequest(developerId, "5500000000000001", 88888, "NGN");

        // Act
        var response = await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(504, response.StatusCode);
        Assert.NotNull(response.ResponseBody);

        var body = JsonDocument.Parse(response.ResponseBody);
        Assert.Equal("failed", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ProcessRequest_DefaultSuccess_UnrecognizedCard()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var request = CreatePaymentRequest(developerId, "5500000000000001", 5000, "NGN");

        // Act
        var response = await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(200, response.StatusCode);
        Assert.NotNull(response.ResponseBody);

        var body = JsonDocument.Parse(response.ResponseBody);
        Assert.Equal("succeeded", body.RootElement.GetProperty("status").GetString());
    }

    #endregion

    #region Validation Rules (same as production)

    [Fact]
    public async Task ProcessRequest_RejectsEmptyCardNumber()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var request = CreatePaymentRequest(developerId, "", 1000, "NGN");

        // Act
        var response = await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(422, response.StatusCode);
    }

    [Fact]
    public async Task ProcessRequest_RejectsZeroAmount()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var request = CreatePaymentRequest(developerId, "4000000000000000", 0, "NGN");

        // Act
        var response = await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(422, response.StatusCode);
    }

    [Fact]
    public async Task ProcessRequest_RejectsNegativeAmount()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var request = CreatePaymentRequest(developerId, "4000000000000000", -100, "NGN");

        // Act
        var response = await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(422, response.StatusCode);
    }

    [Fact]
    public async Task ProcessRequest_RejectsMissingCurrency()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var request = CreatePaymentRequest(developerId, "4000000000000000", 1000, "");

        // Act
        var response = await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(422, response.StatusCode);
    }

    [Fact]
    public async Task ProcessRequest_RejectsInvalidJson()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var request = new SandboxRequest(developerId, "/api/v1/payments", "POST", "{invalid}", null);

        // Act
        var response = await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task ProcessRequest_RejectsMissingBody()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var request = new SandboxRequest(developerId, "/api/v1/payments", "POST", null, null);

        // Act
        var response = await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(400, response.StatusCode);
    }

    [Fact]
    public async Task ProcessRequest_RejectsInvalidMethod()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var request = new SandboxRequest(developerId, "/api/v1/payments", "GET", null, null);

        // Act
        var response = await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(405, response.StatusCode);
    }

    #endregion

    #region Key Isolation

    [Fact]
    public void ValidateKeyIsolation_SandboxKeyInSandbox_Accepted()
    {
        var result = SandboxEnvironment.ValidateKeyIsolation(keyIsSandbox: true, environmentIsSandbox: true);
        Assert.True(result.IsAccepted);
        Assert.Null(result.RejectionReason);
    }

    [Fact]
    public void ValidateKeyIsolation_ProductionKeyInProduction_Accepted()
    {
        var result = SandboxEnvironment.ValidateKeyIsolation(keyIsSandbox: false, environmentIsSandbox: false);
        Assert.True(result.IsAccepted);
        Assert.Null(result.RejectionReason);
    }

    [Fact]
    public void ValidateKeyIsolation_ProductionKeyInSandbox_Rejected()
    {
        var result = SandboxEnvironment.ValidateKeyIsolation(keyIsSandbox: false, environmentIsSandbox: true);
        Assert.False(result.IsAccepted);
        Assert.Contains("Production API keys cannot be used in the sandbox", result.RejectionReason);
    }

    [Fact]
    public void ValidateKeyIsolation_SandboxKeyInProduction_Rejected()
    {
        var result = SandboxEnvironment.ValidateKeyIsolation(keyIsSandbox: true, environmentIsSandbox: false);
        Assert.False(result.IsAccepted);
        Assert.Contains("Sandbox API keys cannot be used in the production", result.RejectionReason);
    }

    #endregion

    #region Per-Developer Data Isolation

    [Fact]
    public async Task DeveloperDataIsolation_DifferentDevelopersCantSeeEachOthersData()
    {
        // Arrange
        var developer1 = Guid.NewGuid();
        var developer2 = Guid.NewGuid();

        // Developer 1 creates a payment
        var request1 = CreatePaymentRequest(developer1, "4000000000000000", 5000, "NGN");
        await _sandbox.ProcessRequestAsync(request1, CancellationToken.None);

        // Act - Developer 2 queries transactions
        var queryRequest = new SandboxRequest(developer2, "/api/v1/transactions", "GET", null, null);
        var response = await _sandbox.ProcessRequestAsync(queryRequest, CancellationToken.None);

        // Assert - Developer 2 should not see Developer 1's transaction (only baseline data)
        Assert.Equal(200, response.StatusCode);
        var body = JsonDocument.Parse(response.ResponseBody!);
        var data = body.RootElement.GetProperty("data");

        // Developer 2 should only have baseline transactions (2 by default)
        Assert.Equal(2, data.GetArrayLength());
    }

    [Fact]
    public async Task DeveloperDataIsolation_DeveloperCanSeeOwnTransactions()
    {
        // Arrange
        var developerId = Guid.NewGuid();

        // Create a payment
        var request = CreatePaymentRequest(developerId, "4000000000000000", 5000, "NGN");
        await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Act - Same developer queries transactions
        var queryRequest = new SandboxRequest(developerId, "/api/v1/transactions", "GET", null, null);
        var response = await _sandbox.ProcessRequestAsync(queryRequest, CancellationToken.None);

        // Assert - Should see baseline + the new payment (3 total)
        Assert.Equal(200, response.StatusCode);
        var body = JsonDocument.Parse(response.ResponseBody!);
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(3, data.GetArrayLength());
    }

    [Fact]
    public async Task DeveloperDataIsolation_RefundFailsForOtherDeveloperTransaction()
    {
        // Arrange
        var developer1 = Guid.NewGuid();
        var developer2 = Guid.NewGuid();

        // Developer 1 creates a successful payment
        var paymentRequest = CreatePaymentRequest(developer1, "4000000000000000", 5000, "NGN");
        var paymentResponse = await _sandbox.ProcessRequestAsync(paymentRequest, CancellationToken.None);
        var paymentBody = JsonDocument.Parse(paymentResponse.ResponseBody!);
        var transactionId = paymentBody.RootElement.GetProperty("transactionId").GetGuid();

        // Act - Developer 2 tries to refund developer 1's transaction
        var refundJson = JsonSerializer.Serialize(new { transactionId, amount = 5000 });
        var refundRequest = new SandboxRequest(developer2, "/api/v1/refunds", "POST", refundJson, null);
        var refundResponse = await _sandbox.ProcessRequestAsync(refundRequest, CancellationToken.None);

        // Assert - Should get 404 (transaction not found in developer 2's data)
        Assert.Equal(404, refundResponse.StatusCode);
    }

    #endregion

    #region Data Reset

    [Fact]
    public async Task ResetData_ResetsToKnownBaseline()
    {
        // Arrange
        var developerId = Guid.NewGuid();

        // Create several transactions
        for (int i = 0; i < 5; i++)
        {
            var request = CreatePaymentRequest(developerId, "4000000000000000", 1000 + i, "NGN");
            await _sandbox.ProcessRequestAsync(request, CancellationToken.None);
        }

        // Verify we have more than baseline
        var queryBefore = new SandboxRequest(developerId, "/api/v1/transactions", "GET", null, null);
        var beforeResponse = await _sandbox.ProcessRequestAsync(queryBefore, CancellationToken.None);
        var beforeBody = JsonDocument.Parse(beforeResponse.ResponseBody!);
        Assert.Equal(7, beforeBody.RootElement.GetProperty("total").GetInt32()); // 2 baseline + 5 new

        // Act
        await _sandbox.ResetDataAsync(developerId, CancellationToken.None);

        // Assert - should be back to baseline
        var queryAfter = new SandboxRequest(developerId, "/api/v1/transactions", "GET", null, null);
        var afterResponse = await _sandbox.ProcessRequestAsync(queryAfter, CancellationToken.None);
        var afterBody = JsonDocument.Parse(afterResponse.ResponseBody!);
        Assert.Equal(2, afterBody.RootElement.GetProperty("total").GetInt32()); // Only baseline
    }

    [Fact]
    public async Task ResetData_DoesNotAffectOtherDevelopers()
    {
        // Arrange
        var developer1 = Guid.NewGuid();
        var developer2 = Guid.NewGuid();

        // Both developers create transactions
        var request1 = CreatePaymentRequest(developer1, "4000000000000000", 1000, "NGN");
        await _sandbox.ProcessRequestAsync(request1, CancellationToken.None);

        var request2 = CreatePaymentRequest(developer2, "4000000000000000", 2000, "NGN");
        await _sandbox.ProcessRequestAsync(request2, CancellationToken.None);

        // Act - Reset only developer 1
        await _sandbox.ResetDataAsync(developer1, CancellationToken.None);

        // Assert - Developer 2 still has their data
        var queryDev2 = new SandboxRequest(developer2, "/api/v1/transactions", "GET", null, null);
        var response2 = await _sandbox.ProcessRequestAsync(queryDev2, CancellationToken.None);
        var body2 = JsonDocument.Parse(response2.ResponseBody!);
        Assert.Equal(3, body2.RootElement.GetProperty("total").GetInt32()); // baseline + 1
    }

    [Fact]
    public async Task ResetData_ThrowsForEmptyDeveloperId()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _sandbox.ResetDataAsync(Guid.Empty, CancellationToken.None));
    }

    #endregion

    #region Webhook Delivery

    [Fact]
    public async Task ProcessRequest_TriggersWebhookDelivery_ForMatchingSubscription()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var subscription = WebhookSubscription.Create(
            developerId,
            "https://example.com/webhook",
            new[] { "payment.completed" },
            "test-secret");
        _subscriptionRepo.AddSubscription(subscription);

        var request = CreatePaymentRequest(developerId, "4000000000000000", 5000, "NGN");

        // Act
        await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Single(_webhookEngine.Deliveries);
        Assert.Equal("payment.completed", _webhookEngine.Deliveries[0].EventType);
    }

    [Fact]
    public async Task ProcessRequest_DoesNotTriggerWebhook_ForNonMatchingEventType()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var subscription = WebhookSubscription.Create(
            developerId,
            "https://example.com/webhook",
            new[] { "dispute.created" }, // Not a payment event
            "test-secret");
        _subscriptionRepo.AddSubscription(subscription);

        var request = CreatePaymentRequest(developerId, "4000000000000000", 5000, "NGN");

        // Act
        await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert - No deliveries since event type doesn't match
        Assert.Empty(_webhookEngine.Deliveries);
    }

    [Fact]
    public async Task ProcessRequest_DoesNotTriggerWebhook_ForOtherDeveloper()
    {
        // Arrange
        var developer1 = Guid.NewGuid();
        var developer2 = Guid.NewGuid();

        // Subscription belongs to developer2
        var subscription = WebhookSubscription.Create(
            developer2,
            "https://example.com/webhook",
            new[] { "payment.completed" },
            "test-secret");
        _subscriptionRepo.AddSubscription(subscription);

        // Developer1 processes a request
        var request = CreatePaymentRequest(developer1, "4000000000000000", 5000, "NGN");

        // Act
        await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert - No deliveries since subscription belongs to different developer
        Assert.Empty(_webhookEngine.Deliveries);
    }

    [Fact]
    public async Task ProcessRequest_FailedPayment_TriggersPaymentFailedWebhook()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var subscription = WebhookSubscription.Create(
            developerId,
            "https://example.com/webhook",
            new[] { "payment.failed" },
            "test-secret");
        _subscriptionRepo.AddSubscription(subscription);

        // Use declined card
        var request = CreatePaymentRequest(developerId, "4000000000000002", 5000, "NGN");

        // Act
        await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Single(_webhookEngine.Deliveries);
        Assert.Equal("payment.failed", _webhookEngine.Deliveries[0].EventType);
    }

    [Fact]
    public async Task ProcessRequest_WebhookPayload_ContainsRequiredFields()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var subscription = WebhookSubscription.Create(
            developerId,
            "https://example.com/webhook",
            new[] { "payment.completed" },
            "test-secret");
        _subscriptionRepo.AddSubscription(subscription);

        var request = CreatePaymentRequest(developerId, "4000000000000000", 5000, "NGN");

        // Act
        await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        var delivery = _webhookEngine.Deliveries[0];
        var payload = JsonDocument.Parse(delivery.Payload);
        Assert.True(payload.RootElement.TryGetProperty("id", out _));
        Assert.Equal("payment.completed", payload.RootElement.GetProperty("type").GetString());
        Assert.True(payload.RootElement.TryGetProperty("data", out _));
        Assert.True(payload.RootElement.TryGetProperty("timestamp", out _));
        Assert.True(payload.RootElement.GetProperty("sandbox").GetBoolean());
    }

    #endregion

    #region Response Format

    [Fact]
    public async Task ProcessRequest_ResponseIncludesSandboxHeader()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var request = CreatePaymentRequest(developerId, "4000000000000000", 1000, "NGN");

        // Act
        var response = await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(response.Headers);
        Assert.True(response.Headers.ContainsKey("X-Sandbox"));
        Assert.Equal("true", response.Headers["X-Sandbox"]);
    }

    [Fact]
    public async Task ProcessRequest_UnknownEndpoint_Returns404()
    {
        // Arrange
        var developerId = Guid.NewGuid();
        var request = new SandboxRequest(developerId, "/api/v1/unknown", "GET", null, null);

        // Act
        var response = await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task ProcessRequest_EmptyDeveloperId_Returns400()
    {
        // Arrange
        var request = new SandboxRequest(Guid.Empty, "/api/v1/payments", "POST", null, null);

        // Act
        var response = await _sandbox.ProcessRequestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(400, response.StatusCode);
    }

    #endregion

    #region Helpers

    private static SandboxRequest CreatePaymentRequest(
        Guid developerId, string cardNumber, long amount, string currency)
    {
        var body = JsonSerializer.Serialize(new
        {
            cardNumber,
            amount,
            currency,
            description = "Test payment",
            merchantReference = $"TEST-{Guid.NewGuid():N}"
        });

        return new SandboxRequest(developerId, "/api/v1/payments", "POST", body, null);
    }

    #endregion

    #region Fakes

    private class FakeWebhookDeliveryEngine : IWebhookDeliveryEngine
    {
        public List<WebhookDelivery> Deliveries { get; } = new();

        public Task DeliverAsync(WebhookDelivery delivery, CancellationToken ct)
        {
            Deliveries.Add(delivery);
            return Task.CompletedTask;
        }

        public Task RetryAsync(Guid deliveryId, CancellationToken ct) => Task.CompletedTask;
        public Task ReplayFromDlqAsync(Guid dlqItemId, CancellationToken ct) => Task.CompletedTask;
    }

    private class FakeWebhookSubscriptionRepository : IWebhookSubscriptionRepository
    {
        private readonly List<WebhookSubscription> _subscriptions = new();

        public void AddSubscription(WebhookSubscription subscription) => _subscriptions.Add(subscription);

        public Task<WebhookSubscription> CreateAsync(WebhookSubscription subscription, CancellationToken ct)
        {
            _subscriptions.Add(subscription);
            return Task.FromResult(subscription);
        }

        public Task<WebhookSubscription?> GetByIdAsync(Guid subscriptionId, CancellationToken ct)
        {
            return Task.FromResult(_subscriptions.FirstOrDefault(s => s.Id == subscriptionId));
        }

        public Task<IReadOnlyList<WebhookSubscription>> GetActiveByEventTypeAsync(string eventType, CancellationToken ct)
        {
            var result = _subscriptions
                .Where(s => s.Status == SubscriptionStatus.Active &&
                            s.EventTypes.Contains(eventType))
                .ToList() as IReadOnlyList<WebhookSubscription>;
            return Task.FromResult(result);
        }

        public Task<int> CountActiveByMerchantAsync(Guid merchantId, CancellationToken ct)
        {
            return Task.FromResult(_subscriptions.Count(s => s.MerchantId == merchantId &&
                s.Status == SubscriptionStatus.Active));
        }

        public Task<IReadOnlyList<WebhookSubscription>> GetByMerchantAsync(Guid merchantId, CancellationToken ct)
        {
            var result = _subscriptions.Where(s => s.MerchantId == merchantId).ToList() as IReadOnlyList<WebhookSubscription>;
            return Task.FromResult(result);
        }

        public Task UpdateAsync(WebhookSubscription subscription, CancellationToken ct) => Task.CompletedTask;
    }

    #endregion
}
