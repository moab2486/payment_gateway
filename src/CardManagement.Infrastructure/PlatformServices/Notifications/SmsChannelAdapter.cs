using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CardManagement.Application.PlatformServices.Notifications.Ports;
using CardManagement.Domain.PlatformServices.Notifications;
using CardManagement.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.PlatformServices.Notifications;

/// <summary>
/// SMS notification channel adapter using the configured SMS provider API.
/// Wraps HTTP calls with a circuit breaker to avoid hammering a degraded SMS gateway.
/// </summary>
public sealed class SmsChannelAdapter : INotificationChannelAdapter
{
    private const string ChannelName = "Sms";

    private readonly HttpClient _httpClient;
    private readonly INotificationCircuitBreakerRegistry _circuitBreakerRegistry;
    private readonly SmsChannelOptions _options;
    private readonly ILogger<SmsChannelAdapter> _logger;

    public SmsChannelAdapter(
        HttpClient httpClient,
        INotificationCircuitBreakerRegistry circuitBreakerRegistry,
        IOptions<SmsChannelOptions> options,
        ILogger<SmsChannelAdapter> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _circuitBreakerRegistry = circuitBreakerRegistry ?? throw new ArgumentNullException(nameof(circuitBreakerRegistry));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public NotificationChannel Channel => NotificationChannel.Sms;

    /// <inheritdoc />
    public async Task<DeliveryResult> SendAsync(RenderedNotification notification, CancellationToken ct)
    {
        var breaker = _circuitBreakerRegistry.GetBreaker(ChannelName);

        try
        {
            var providerMessageId = await breaker.ExecuteAsync(async token =>
            {
                return await SendSmsAsync(notification, token);
            }, ct);

            _logger.LogInformation(
                "SMS sent successfully to {Recipient}. MessageId={MessageId}",
                notification.RecipientAddress, providerMessageId);

            return DeliveryResult.Succeeded(providerMessageId);
        }
        catch (CircuitBreakerOpenException)
        {
            _logger.LogWarning(
                "SMS circuit breaker is open. Cannot send to {Recipient}.",
                notification.RecipientAddress);

            return DeliveryResult.Failed("SMS channel circuit breaker is open");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Propagate caller cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send SMS to {Recipient}: {Message}",
                notification.RecipientAddress, ex.Message);

            return DeliveryResult.Failed($"SMS provider error: {ex.Message}");
        }
    }

    private async Task<string> SendSmsAsync(RenderedNotification notification, CancellationToken ct)
    {
        var payload = new
        {
            to = notification.RecipientAddress,
            from = _options.SenderId,
            message = notification.Body
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/messages/send")
        {
            Content = jsonContent
        };

        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.ApiKey}");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        using var response = await _httpClient.SendAsync(request, timeoutCts.Token);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"SMS provider returned {(int)response.StatusCode}: {errorBody}");
        }

        // Parse the response to extract the provider message ID
        var responseBody = await response.Content.ReadFromJsonAsync<SmsProviderResponse>(cancellationToken: ct);
        return responseBody?.MessageId ?? $"sms-{Guid.NewGuid():N}";
    }

    private sealed record SmsProviderResponse(string? MessageId, string? Status);
}
