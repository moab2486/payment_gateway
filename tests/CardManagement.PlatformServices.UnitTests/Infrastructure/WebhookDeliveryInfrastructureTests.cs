using System.Net;
using System.Security.Cryptography;
using System.Text;
using CardManagement.Application.DTOs;
using CardManagement.Application.PlatformServices.Webhooks.Commands;
using CardManagement.Application.PlatformServices.Webhooks.Handlers;
using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.PlatformServices.Webhooks;
using CardManagement.Infrastructure.PlatformServices.Webhooks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Infrastructure;

#region Fakes

internal sealed class FakeUrlVerificationHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

    public FakeUrlVerificationHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        _handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        return Task.FromResult(_handler(request));
    }
}

internal sealed class FakeWebhookSubscriptionRepository : IWebhookSubscriptionRepository
{
    private readonly List<WebhookSubscription> _subscriptions = new();
    private int _activeCountOverride = -1;

    public void SetActiveCount(int count) => _activeCountOverride = count;

    public Task<WebhookSubscription> CreateAsync(WebhookSubscription subscription, CancellationToken ct)
    {
        _subscriptions.Add(subscription);
        return Task.FromResult(subscription);
    }

    public Task<WebhookSubscription?> GetByIdAsync(Guid subscriptionId, CancellationToken ct) =>
        Task.FromResult(_subscriptions.FirstOrDefault(s => s.Id == subscriptionId));

    public Task<IReadOnlyList<WebhookSubscription>> GetActiveByEventTypeAsync(string eventType, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<WebhookSubscription>>(_subscriptions);

    public Task<int> CountActiveByMerchantAsync(Guid merchantId, CancellationToken ct) =>
        Task.FromResult(_activeCountOverride >= 0 ? _activeCountOverride : _subscriptions.Count(s => s.MerchantId == merchantId));

    public Task<IReadOnlyList<WebhookSubscription>> GetByMerchantAsync(Guid merchantId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<WebhookSubscription>>(_subscriptions.Where(s => s.MerchantId == merchantId).ToList());

    public Task UpdateAsync(WebhookSubscription subscription, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class FakeUrlVerificationService : IUrlVerificationService
{
    private readonly bool _result;
    public FakeUrlVerificationService(bool result) => _result = result;

    public Task<bool> VerifyChallengeAsync(string destinationUrl, CancellationToken ct) =>
        Task.FromResult(_result);
}

internal sealed class FakeAuditStore : IAuditStore
{
    public List<AuditEntry> Entries { get; } = new();

    public Task AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditEntry>> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AuditEntry>>(Entries.Where(e => e.TransactionReference == transactionReference).ToList());
}

#endregion

public class HmacSignerTests
{
    private readonly HmacSigner _signer = new();

    [Fact]
    public void ComputeSignature_WithKnownVector_ProducesExpectedResult()
    {
        // Known HMAC-SHA256 test vector
        var payload = "Hello, World!";
        var secret = "my-secret-key";

        // Compute expected value manually
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(keyBytes);
        var expectedHash = Convert.ToHexString(hmac.ComputeHash(payloadBytes)).ToLowerInvariant();
        var expectedSignature = $"sha256={expectedHash}";

        var result = _signer.ComputeSignature(payload, secret);

        Assert.Equal(expectedSignature, result);
    }

    [Fact]
    public void ComputeSignature_DifferentPayloads_ProduceDifferentSignatures()
    {
        var secret = "shared-secret";
        var sig1 = _signer.ComputeSignature("payload-one", secret);
        var sig2 = _signer.ComputeSignature("payload-two", secret);

        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void ComputeSignature_DifferentSecrets_ProduceDifferentSignatures()
    {
        var payload = "same-payload";
        var sig1 = _signer.ComputeSignature(payload, "secret-one");
        var sig2 = _signer.ComputeSignature(payload, "secret-two");

        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void ComputeSignature_HasSha256Prefix()
    {
        var result = _signer.ComputeSignature("test", "key");

        Assert.StartsWith("sha256=", result);
    }

    [Fact]
    public void VerifySignature_WithMatchingPayloadAndSecret_ReturnsTrue()
    {
        var payload = "{\"event\":\"payment.completed\",\"amount\":1000}";
        var secret = "webhook-signing-secret-123";

        var signature = _signer.ComputeSignature(payload, secret);
        var isValid = _signer.VerifySignature(payload, secret, signature);

        Assert.True(isValid);
    }

    [Fact]
    public void VerifySignature_WithTamperedPayload_ReturnsFalse()
    {
        var secret = "webhook-signing-secret-123";
        var originalPayload = "{\"event\":\"payment.completed\",\"amount\":1000}";
        var tamperedPayload = "{\"event\":\"payment.completed\",\"amount\":9999}";

        var signature = _signer.ComputeSignature(originalPayload, secret);
        var isValid = _signer.VerifySignature(tamperedPayload, secret, signature);

        Assert.False(isValid);
    }

    [Fact]
    public void VerifySignature_WithWrongSecret_ReturnsFalse()
    {
        var payload = "{\"event\":\"payment.completed\"}";
        var correctSecret = "correct-secret";
        var wrongSecret = "wrong-secret";

        var signature = _signer.ComputeSignature(payload, correctSecret);
        var isValid = _signer.VerifySignature(payload, wrongSecret, signature);

        Assert.False(isValid);
    }

    [Fact]
    public void VerifySignature_WithInvalidSignatureFormat_ReturnsFalse()
    {
        var payload = "test-payload";
        var secret = "test-secret";

        var isValid = _signer.VerifySignature(payload, secret, "sha256=0000000000000000000000000000000000000000000000000000000000000000");

        Assert.False(isValid);
    }

    [Fact]
    public void ComputeSignature_ThrowsOnNullPayload()
    {
        Assert.Throws<ArgumentNullException>(() => _signer.ComputeSignature(null!, "secret"));
    }

    [Fact]
    public void ComputeSignature_ThrowsOnEmptySecret()
    {
        Assert.Throws<ArgumentException>(() => _signer.ComputeSignature("payload", ""));
    }

    [Fact]
    public void VerifySignature_ThrowsOnNullSignature()
    {
        Assert.Throws<ArgumentNullException>(() => _signer.VerifySignature("payload", "secret", null!));
    }
}

public class ExponentialBackoffCalculatorTests
{
    [Theory]
    [InlineData(1, 5000)]    // 5000 * 2^0 = 5000
    [InlineData(2, 10000)]   // 5000 * 2^1 = 10000
    [InlineData(3, 20000)]   // 5000 * 2^2 = 20000
    [InlineData(4, 40000)]   // 5000 * 2^3 = 40000
    [InlineData(5, 80000)]   // 5000 * 2^4 = 80000
    public void ComputeDelay_WithDeterministicJitter_CalculatesCorrectBaseDelay(int attempt, double expectedMs)
    {
        // Use jitter factor of 1.0 (no jitter) for deterministic testing
        var calculator = new ExponentialBackoffCalculator(5000, 2.0, 1.0, 1.0);

        var delay = calculator.ComputeDelay(attempt);

        Assert.Equal(expectedMs, delay.TotalMilliseconds, precision: 1);
    }

    [Fact]
    public void ComputeDelay_WithDefaultJitter_IsWithinBounds()
    {
        var baseDelay = 5000.0;
        var multiplier = 2.0;
        var calculator = new ExponentialBackoffCalculator(baseDelay, multiplier);

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            var expectedBase = baseDelay * Math.Pow(multiplier, attempt - 1);
            var minExpected = expectedBase * 0.8;
            var maxExpected = expectedBase * 1.2;

            // Run multiple times to check jitter bounds
            for (int i = 0; i < 50; i++)
            {
                var delay = calculator.ComputeDelay(attempt);

                Assert.InRange(delay.TotalMilliseconds, minExpected, maxExpected);
            }
        }
    }

    [Fact]
    public void ComputeDelay_Attempt1_WithExactJitterFactor_ReturnsBaseDelay()
    {
        var calculator = new ExponentialBackoffCalculator(1000, 3.0, 0.8, 1.2);

        // jitterFactor=1.0 means no jitter
        var delay = calculator.ComputeDelay(1, 1.0);

        Assert.Equal(1000.0, delay.TotalMilliseconds);
    }

    [Fact]
    public void ComputeDelay_WithMinJitter_ReturnsLowerBound()
    {
        var calculator = new ExponentialBackoffCalculator(5000, 2.0, 0.8, 1.2);

        // Attempt 2: base = 5000 * 2^1 = 10000; with min jitter (0.8) = 8000
        var delay = calculator.ComputeDelay(2, 0.8);

        Assert.Equal(8000.0, delay.TotalMilliseconds);
    }

    [Fact]
    public void ComputeDelay_WithMaxJitter_ReturnsUpperBound()
    {
        var calculator = new ExponentialBackoffCalculator(5000, 2.0, 0.8, 1.2);

        // Attempt 2: base = 5000 * 2^1 = 10000; with max jitter (1.2) = 12000
        var delay = calculator.ComputeDelay(2, 1.2);

        Assert.Equal(12000.0, delay.TotalMilliseconds);
    }

    [Fact]
    public void ComputeDelay_ThrowsOnZeroAttempt()
    {
        var calculator = new ExponentialBackoffCalculator();

        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.ComputeDelay(0));
    }

    [Fact]
    public void ComputeDelay_ThrowsOnNegativeAttempt()
    {
        var calculator = new ExponentialBackoffCalculator();

        Assert.Throws<ArgumentOutOfRangeException>(() => calculator.ComputeDelay(-1));
    }

    [Fact]
    public void Constructor_ThrowsOnZeroBaseDelay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExponentialBackoffCalculator(0, 2.0));
    }

    [Fact]
    public void Constructor_ThrowsOnNegativeMultiplier()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExponentialBackoffCalculator(1000, -1.0));
    }

    [Fact]
    public void ComputeDelay_ConsecutiveAttempts_DelayIncreases()
    {
        // Use deterministic jitter
        var calculator = new ExponentialBackoffCalculator(1000, 2.0, 1.0, 1.0);

        var delay1 = calculator.ComputeDelay(1);
        var delay2 = calculator.ComputeDelay(2);
        var delay3 = calculator.ComputeDelay(3);

        Assert.True(delay2 > delay1);
        Assert.True(delay3 > delay2);
    }
}

public class UrlVerificationServiceTests
{
    [Fact]
    public async Task VerifyChallengeAsync_WhenEndpointReturnsChallenge_ReturnsTrue()
    {
        var handler = new FakeUrlVerificationHttpMessageHandler(request =>
        {
            // Extract challenge from query string
            var uri = request.RequestUri!;
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var challenge = query["challenge"];

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(challenge ?? "")
            };
        });

        var httpClient = new HttpClient(handler);
        var service = new UrlVerificationService(httpClient, NullLogger<UrlVerificationService>.Instance);

        var result = await service.VerifyChallengeAsync("https://example.com/webhook", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task VerifyChallengeAsync_WhenEndpointReturnsWrongChallenge_ReturnsFalse()
    {
        var handler = new FakeUrlVerificationHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("wrong-challenge-token")
            });

        var httpClient = new HttpClient(handler);
        var service = new UrlVerificationService(httpClient, NullLogger<UrlVerificationService>.Instance);

        var result = await service.VerifyChallengeAsync("https://example.com/webhook", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task VerifyChallengeAsync_WhenEndpointReturns500_ReturnsFalse()
    {
        var handler = new FakeUrlVerificationHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var httpClient = new HttpClient(handler);
        var service = new UrlVerificationService(httpClient, NullLogger<UrlVerificationService>.Instance);

        var result = await service.VerifyChallengeAsync("https://example.com/webhook", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task VerifyChallengeAsync_WhenEndpointReturns404_ReturnsFalse()
    {
        var handler = new FakeUrlVerificationHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound));

        var httpClient = new HttpClient(handler);
        var service = new UrlVerificationService(httpClient, NullLogger<UrlVerificationService>.Instance);

        var result = await service.VerifyChallengeAsync("https://example.com/webhook", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task VerifyChallengeAsync_WhenConnectionFails_ReturnsFalse()
    {
        var handler = new FakeUrlVerificationHttpMessageHandler(_ =>
            throw new HttpRequestException("Connection refused"));

        var httpClient = new HttpClient(handler);
        var service = new UrlVerificationService(httpClient, NullLogger<UrlVerificationService>.Instance);

        var result = await service.VerifyChallengeAsync("https://unreachable.example.com/webhook", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task VerifyChallengeAsync_AppendsQueryParameterCorrectly()
    {
        Uri? capturedUri = null;
        var handler = new FakeUrlVerificationHttpMessageHandler(request =>
        {
            capturedUri = request.RequestUri;
            var query = System.Web.HttpUtility.ParseQueryString(capturedUri!.Query);
            var challenge = query["challenge"];
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(challenge ?? "")
            };
        });

        var httpClient = new HttpClient(handler);
        var service = new UrlVerificationService(httpClient, NullLogger<UrlVerificationService>.Instance);

        await service.VerifyChallengeAsync("https://example.com/webhook", CancellationToken.None);

        Assert.NotNull(capturedUri);
        Assert.Contains("challenge=", capturedUri!.Query);
    }

    [Fact]
    public async Task VerifyChallengeAsync_UrlWithExistingQuery_UsesAmpersand()
    {
        Uri? capturedUri = null;
        var handler = new FakeUrlVerificationHttpMessageHandler(request =>
        {
            capturedUri = request.RequestUri;
            var query = System.Web.HttpUtility.ParseQueryString(capturedUri!.Query);
            var challenge = query["challenge"];
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(challenge ?? "")
            };
        });

        var httpClient = new HttpClient(handler);
        var service = new UrlVerificationService(httpClient, NullLogger<UrlVerificationService>.Instance);

        await service.VerifyChallengeAsync("https://example.com/webhook?existing=param", CancellationToken.None);

        Assert.NotNull(capturedUri);
        Assert.Contains("existing=param", capturedUri!.Query);
        Assert.Contains("challenge=", capturedUri!.Query);
    }
}

public class CreateSubscriptionLimitTests
{
    [Fact]
    public async Task HandleAsync_WhenMerchantHas50Subscriptions_ReturnsFailure()
    {
        var repo = new FakeWebhookSubscriptionRepository();
        repo.SetActiveCount(50);
        var urlService = new FakeUrlVerificationService(true);
        var auditStore = new FakeAuditStore();

        var handler = new CreateSubscriptionCommandHandler(repo, urlService, auditStore);

        var command = new CreateSubscriptionCommand(
            Guid.NewGuid(),
            "https://example.com/webhook",
            new[] { "payment.completed" },
            "my-secret");

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SUBSCRIPTION_LIMIT_EXCEEDED", result.ErrorCode);
    }

    [Fact]
    public async Task HandleAsync_WhenMerchantHas49Subscriptions_Succeeds()
    {
        var repo = new FakeWebhookSubscriptionRepository();
        repo.SetActiveCount(49);
        var urlService = new FakeUrlVerificationService(true);
        var auditStore = new FakeAuditStore();

        var handler = new CreateSubscriptionCommandHandler(repo, urlService, auditStore);

        var command = new CreateSubscriptionCommand(
            Guid.NewGuid(),
            "https://example.com/webhook",
            new[] { "payment.completed" },
            "my-secret");

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
    }

    [Fact]
    public async Task HandleAsync_WhenMerchantHas0Subscriptions_Succeeds()
    {
        var repo = new FakeWebhookSubscriptionRepository();
        repo.SetActiveCount(0);
        var urlService = new FakeUrlVerificationService(true);
        var auditStore = new FakeAuditStore();

        var handler = new CreateSubscriptionCommandHandler(repo, urlService, auditStore);

        var command = new CreateSubscriptionCommand(
            Guid.NewGuid(),
            "https://example.com/webhook",
            new[] { "payment.completed", "payment.failed" },
            "signing-secret-123");

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_WhenUrlVerificationFails_ReturnsFailure()
    {
        var repo = new FakeWebhookSubscriptionRepository();
        repo.SetActiveCount(0);
        var urlService = new FakeUrlVerificationService(false);
        var auditStore = new FakeAuditStore();

        var handler = new CreateSubscriptionCommandHandler(repo, urlService, auditStore);

        var command = new CreateSubscriptionCommand(
            Guid.NewGuid(),
            "https://unreachable.example.com/webhook",
            new[] { "payment.completed" },
            "my-secret");

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("URL_VERIFICATION_FAILED", result.ErrorCode);
    }
}

public class WebhookDeliveryStatusTransitionTests
{
    [Fact]
    public void NewDelivery_HasPendingStatus()
    {
        var delivery = WebhookDelivery.Create(
            Guid.NewGuid(), "payment.completed", "{\"amount\":1000}");

        Assert.Equal(DeliveryStatus.Pending, delivery.Status);
        Assert.Equal(0, delivery.AttemptCount);
    }

    [Fact]
    public void MarkDelivered_TransitionsPendingToDelivered()
    {
        var delivery = WebhookDelivery.Create(
            Guid.NewGuid(), "payment.completed", "{\"amount\":1000}");

        delivery.MarkDelivered(200, TimeSpan.FromMilliseconds(150));

        Assert.Equal(DeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Single(delivery.Attempts);
        Assert.Equal(200, delivery.Attempts[0].HttpStatusCode);
    }

    [Fact]
    public void RecordFailedAttempt_TransitionsPendingToFailed()
    {
        var delivery = WebhookDelivery.Create(
            Guid.NewGuid(), "payment.completed", "{\"amount\":1000}");

        delivery.RecordFailedAttempt(500, TimeSpan.FromMilliseconds(5000), "Internal Server Error");

        Assert.Equal(DeliveryStatus.Failed, delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Single(delivery.Attempts);
        Assert.Equal(500, delivery.Attempts[0].HttpStatusCode);
    }

    [Fact]
    public void ScheduleRetry_TransitionsFailedBackToPending()
    {
        var delivery = WebhookDelivery.Create(
            Guid.NewGuid(), "payment.completed", "{\"amount\":1000}");

        delivery.RecordFailedAttempt(503, TimeSpan.FromMilliseconds(100), "Service Unavailable");
        delivery.ScheduleRetry(DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(DeliveryStatus.Pending, delivery.Status);
        Assert.NotNull(delivery.NextRetryAtUtc);
    }

    [Fact]
    public void MoveToDlq_TransitionsFailedToDeadLettered()
    {
        var delivery = WebhookDelivery.Create(
            Guid.NewGuid(), "payment.completed", "{\"amount\":1000}");

        delivery.RecordFailedAttempt(500, TimeSpan.FromMilliseconds(100), "Error");
        delivery.MoveToDlq();

        Assert.Equal(DeliveryStatus.DeadLettered, delivery.Status);
        Assert.Null(delivery.NextRetryAtUtc);
    }

    [Fact]
    public void FullLifecycle_PendingToFailedToDeadLettered_WithMultipleRetries()
    {
        var delivery = WebhookDelivery.Create(
            Guid.NewGuid(), "payment.completed", "{\"amount\":1000}");

        // Simulate 5 failed attempts with retries
        for (int i = 0; i < 5; i++)
        {
            delivery.RecordFailedAttempt(503, TimeSpan.FromMilliseconds(100), $"Attempt {i + 1} failed");

            if (i < 4) // Schedule retries for first 4 failures
            {
                delivery.ScheduleRetry(DateTime.UtcNow.AddMinutes(Math.Pow(2, i)));
            }
        }

        // After exhausting retries, move to DLQ
        delivery.MoveToDlq();

        Assert.Equal(DeliveryStatus.DeadLettered, delivery.Status);
        Assert.Equal(5, delivery.AttemptCount);
        Assert.Equal(5, delivery.Attempts.Count);
    }

    [Fact]
    public void MarkDelivered_AfterRetry_SucceedsOnSecondAttempt()
    {
        var delivery = WebhookDelivery.Create(
            Guid.NewGuid(), "payment.completed", "{\"amount\":1000}");

        // First attempt fails
        delivery.RecordFailedAttempt(503, TimeSpan.FromMilliseconds(100), "Unavailable");
        delivery.ScheduleRetry(DateTime.UtcNow.AddSeconds(5));

        // Second attempt succeeds
        delivery.MarkDelivered(200, TimeSpan.FromMilliseconds(50));

        Assert.Equal(DeliveryStatus.Delivered, delivery.Status);
        Assert.Equal(2, delivery.AttemptCount);
    }

    [Fact]
    public void MarkDelivered_WhenAlreadyDelivered_Throws()
    {
        var delivery = WebhookDelivery.Create(
            Guid.NewGuid(), "payment.completed", "{\"amount\":1000}");

        delivery.MarkDelivered(200, TimeSpan.FromMilliseconds(50));

        Assert.Throws<InvalidOperationException>(() =>
            delivery.MarkDelivered(200, TimeSpan.FromMilliseconds(50)));
    }

    [Fact]
    public void RecordFailedAttempt_WhenDeadLettered_Throws()
    {
        var delivery = WebhookDelivery.Create(
            Guid.NewGuid(), "payment.completed", "{\"amount\":1000}");

        delivery.RecordFailedAttempt(500, TimeSpan.FromMilliseconds(100), "Error");
        delivery.MoveToDlq();

        Assert.Throws<InvalidOperationException>(() =>
            delivery.RecordFailedAttempt(500, TimeSpan.FromMilliseconds(100), "Error"));
    }

    [Fact]
    public void MoveToDlq_WhenNotFailed_Throws()
    {
        var delivery = WebhookDelivery.Create(
            Guid.NewGuid(), "payment.completed", "{\"amount\":1000}");

        // Pending state — cannot go directly to DLQ
        Assert.Throws<InvalidOperationException>(() => delivery.MoveToDlq());
    }

    [Fact]
    public void ScheduleRetry_WhenNotFailed_Throws()
    {
        var delivery = WebhookDelivery.Create(
            Guid.NewGuid(), "payment.completed", "{\"amount\":1000}");

        // Pending state — cannot schedule retry
        Assert.Throws<InvalidOperationException>(() =>
            delivery.ScheduleRetry(DateTime.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public void ResetForReplay_WhenDeadLettered_TransitionsToPending()
    {
        var delivery = WebhookDelivery.Create(
            Guid.NewGuid(), "payment.completed", "{\"amount\":1000}");

        delivery.RecordFailedAttempt(500, TimeSpan.FromMilliseconds(100), "Error");
        delivery.MoveToDlq();
        delivery.ResetForReplay();

        Assert.Equal(DeliveryStatus.Pending, delivery.Status);
    }

    [Fact]
    public void ResetForReplay_WhenNotDeadLettered_Throws()
    {
        var delivery = WebhookDelivery.Create(
            Guid.NewGuid(), "payment.completed", "{\"amount\":1000}");

        Assert.Throws<InvalidOperationException>(() => delivery.ResetForReplay());
    }
}
