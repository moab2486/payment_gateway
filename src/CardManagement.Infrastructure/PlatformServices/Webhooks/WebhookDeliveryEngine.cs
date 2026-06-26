using System.Diagnostics;
using System.Text;
using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Domain.PlatformServices.Webhooks;
using CardManagement.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.PlatformServices.Webhooks;

/// <summary>
/// Webhook delivery engine responsible for dispatching webhook payloads to subscriber endpoints
/// with HMAC signing, circuit breaker protection, and retry/DLQ management.
/// </summary>
public sealed class WebhookDeliveryEngine : IWebhookDeliveryEngine
{
    private readonly HttpClient _httpClient;
    private readonly IHmacSigner _hmacSigner;
    private readonly IWebhookSubscriptionRepository _subscriptionRepository;
    private readonly IWebhookDeliveryRepository _deliveryRepository;
    private readonly IWebhookCircuitBreakerRegistry _circuitBreakerRegistry;
    private readonly ExponentialBackoffCalculator _backoffCalculator;
    private readonly WebhookDeliveryOptions _options;
    private readonly ILogger<WebhookDeliveryEngine> _logger;

    private const string SignatureHeaderName = "X-Webhook-Signature";

    public WebhookDeliveryEngine(
        HttpClient httpClient,
        IHmacSigner hmacSigner,
        IWebhookSubscriptionRepository subscriptionRepository,
        IWebhookDeliveryRepository deliveryRepository,
        IWebhookCircuitBreakerRegistry circuitBreakerRegistry,
        IOptions<WebhookDeliveryOptions> options,
        ILogger<WebhookDeliveryEngine> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _hmacSigner = hmacSigner ?? throw new ArgumentNullException(nameof(hmacSigner));
        _subscriptionRepository = subscriptionRepository ?? throw new ArgumentNullException(nameof(subscriptionRepository));
        _deliveryRepository = deliveryRepository ?? throw new ArgumentNullException(nameof(deliveryRepository));
        _circuitBreakerRegistry = circuitBreakerRegistry ?? throw new ArgumentNullException(nameof(circuitBreakerRegistry));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _backoffCalculator = new ExponentialBackoffCalculator(_options.BaseDelayMs, _options.BackoffMultiplier);
    }

    /// <inheritdoc />
    public async Task DeliverAsync(WebhookDelivery delivery, CancellationToken ct)
    {
        var subscription = await _subscriptionRepository.GetByIdAsync(delivery.SubscriptionId, ct);
        if (subscription is null)
        {
            _logger.LogWarning("Subscription {SubscriptionId} not found for delivery {DeliveryId}.",
                delivery.SubscriptionId, delivery.Id);
            return;
        }

        if (subscription.Status == SubscriptionStatus.Deactivated)
        {
            _logger.LogInformation("Skipping delivery {DeliveryId} — subscription {SubscriptionId} is deactivated.",
                delivery.Id, delivery.SubscriptionId);
            return;
        }

        var breaker = _circuitBreakerRegistry.GetBreaker(subscription.DestinationUrl);

        try
        {
            var (statusCode, responseTime) = await breaker.ExecuteAsync(async token =>
            {
                return await SendPayloadAsync(
                    subscription.DestinationUrl,
                    delivery.Payload,
                    subscription.SigningSecret,
                    token);
            }, ct);

            if (statusCode >= 200 && statusCode < 300)
            {
                delivery.MarkDelivered(statusCode, responseTime);
                subscription.RecordSuccessfulDelivery();
                await _subscriptionRepository.UpdateAsync(subscription, ct);
                await _deliveryRepository.UpdateAsync(delivery, ct);

                _logger.LogInformation(
                    "Webhook delivered successfully: DeliveryId={DeliveryId}, StatusCode={StatusCode}",
                    delivery.Id, statusCode);
            }
            else
            {
                await HandleFailedAttemptAsync(delivery, subscription, statusCode, responseTime, null, ct);
            }
        }
        catch (CircuitBreakerOpenException)
        {
            _logger.LogWarning(
                "Circuit breaker open for {Url}. Scheduling retry for delivery {DeliveryId}.",
                subscription.DestinationUrl, delivery.Id);

            await HandleFailedAttemptAsync(delivery, subscription, 0, TimeSpan.Zero, "Circuit breaker open", ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex,
                "Webhook delivery failed for {DeliveryId} to {Url}: {Message}",
                delivery.Id, subscription.DestinationUrl, ex.Message);

            await HandleFailedAttemptAsync(delivery, subscription, 0, TimeSpan.Zero, ex.Message, ct);
        }
    }

    /// <inheritdoc />
    public Task RetryAsync(Guid deliveryId, CancellationToken ct)
    {
        // Retry is handled by the worker pool (task 6.3) which re-enqueues the delivery.
        // This method will be fully implemented when the worker pool is wired in.
        _logger.LogInformation("Retry requested for delivery {DeliveryId}.", deliveryId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReplayFromDlqAsync(Guid dlqItemId, CancellationToken ct)
    {
        // DLQ replay creates a new delivery with fresh HMAC signature.
        // The actual implementation will be wired in the worker pool (task 6.3).
        _logger.LogInformation("Replay from DLQ requested for item {DlqItemId}.", dlqItemId);
        return Task.CompletedTask;
    }

    private async Task<(int StatusCode, TimeSpan ResponseTime)> SendPayloadAsync(
        string destinationUrl,
        string payload,
        string signingSecret,
        CancellationToken ct)
    {
        var signature = _hmacSigner.ComputeSignature(payload, signingSecret);

        using var request = new HttpRequestMessage(HttpMethod.Post, destinationUrl);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        request.Headers.TryAddWithoutValidation(SignatureHeaderName, signature);

        var stopwatch = Stopwatch.StartNew();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.HttpTimeoutSeconds));

        using var response = await _httpClient.SendAsync(request, timeoutCts.Token);
        stopwatch.Stop();

        return ((int)response.StatusCode, stopwatch.Elapsed);
    }

    private async Task HandleFailedAttemptAsync(
        WebhookDelivery delivery,
        WebhookSubscription subscription,
        int statusCode,
        TimeSpan responseTime,
        string? errorMessage,
        CancellationToken ct)
    {
        delivery.RecordFailedAttempt(statusCode, responseTime, errorMessage);
        subscription.RecordFailedDelivery();

        if (delivery.AttemptCount >= _options.MaxRetries)
        {
            delivery.MoveToDlq();
            _logger.LogWarning(
                "Delivery {DeliveryId} exhausted all {MaxRetries} retries. Moving to DLQ.",
                delivery.Id, _options.MaxRetries);
        }
        else
        {
            var delay = _backoffCalculator.ComputeDelay(delivery.AttemptCount);
            var nextRetryAt = DateTime.UtcNow.Add(delay);
            delivery.ScheduleRetry(nextRetryAt);

            _logger.LogInformation(
                "Delivery {DeliveryId} scheduled for retry at {NextRetry} (attempt {Attempt}/{Max}).",
                delivery.Id, nextRetryAt, delivery.AttemptCount, _options.MaxRetries);
        }

        // Check if subscription should be suspended
        if (subscription.Status == SubscriptionStatus.Active &&
            subscription.HasExceededFailureThreshold(_options.SuspensionThreshold))
        {
            subscription.Suspend();
            _logger.LogWarning(
                "Subscription {SubscriptionId} suspended after {Failures} consecutive failures.",
                subscription.Id, subscription.ConsecutiveFailures);
        }

        await _subscriptionRepository.UpdateAsync(subscription, ct);
        await _deliveryRepository.UpdateAsync(delivery, ct);
    }
}
