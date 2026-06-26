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
/// WhatsApp notification channel adapter using the self-hosted WAHA API.
/// Sends text messages via POST /api/sendText and performs a session health check on startup.
/// All HTTP calls are wrapped with a circuit breaker to protect against WAHA downtime.
/// </summary>
public sealed class WhatsAppChannelAdapter : INotificationChannelAdapter
{
    private const string ChannelName = "WhatsApp";

    private readonly HttpClient _httpClient;
    private readonly INotificationCircuitBreakerRegistry _circuitBreakerRegistry;
    private readonly WhatsAppChannelOptions _options;
    private readonly ILogger<WhatsAppChannelAdapter> _logger;

    public WhatsAppChannelAdapter(
        HttpClient httpClient,
        INotificationCircuitBreakerRegistry circuitBreakerRegistry,
        IOptions<WhatsAppChannelOptions> options,
        ILogger<WhatsAppChannelAdapter> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _circuitBreakerRegistry = circuitBreakerRegistry ?? throw new ArgumentNullException(nameof(circuitBreakerRegistry));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public NotificationChannel Channel => NotificationChannel.WhatsApp;

    /// <inheritdoc />
    public async Task<DeliveryResult> SendAsync(RenderedNotification notification, CancellationToken ct)
    {
        var breaker = _circuitBreakerRegistry.GetBreaker(ChannelName);

        try
        {
            var providerMessageId = await breaker.ExecuteAsync(async token =>
            {
                return await SendWhatsAppMessageAsync(notification, token);
            }, ct);

            _logger.LogInformation(
                "WhatsApp message sent successfully to {Recipient}. MessageId={MessageId}",
                notification.RecipientAddress, providerMessageId);

            return DeliveryResult.Succeeded(providerMessageId);
        }
        catch (CircuitBreakerOpenException)
        {
            _logger.LogWarning(
                "WhatsApp circuit breaker is open. Cannot send to {Recipient}.",
                notification.RecipientAddress);

            return DeliveryResult.Failed("WhatsApp channel circuit breaker is open");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Propagate caller cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send WhatsApp message to {Recipient}: {Message}",
                notification.RecipientAddress, ex.Message);

            return DeliveryResult.Failed($"WAHA API error: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks that the WAHA session is active and healthy.
    /// Should be called during application startup to fail fast if WAHA is unreachable.
    /// </summary>
    public async Task<bool> CheckSessionHealthAsync(CancellationToken ct)
    {
        try
        {
            var url = $"{_options.BaseUrl.TrimEnd('/')}/api/sessions/{_options.Session}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddAuthHeader(request);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            using var response = await _httpClient.SendAsync(request, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "WAHA session health check failed. Status={StatusCode}, Session={Session}",
                    (int)response.StatusCode, _options.Session);
                return false;
            }

            var sessionInfo = await response.Content.ReadFromJsonAsync<WahaSessionResponse>(cancellationToken: ct);

            if (sessionInfo?.Status is not ("WORKING" or "SCAN_QR_CODE"))
            {
                _logger.LogWarning(
                    "WAHA session '{Session}' is not in a healthy state. Status={Status}",
                    _options.Session, sessionInfo?.Status ?? "unknown");
                return false;
            }

            _logger.LogInformation(
                "WAHA session '{Session}' is healthy. Status={Status}",
                _options.Session, sessionInfo.Status);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "WAHA session health check encountered an error for session '{Session}'.",
                _options.Session);
            return false;
        }
    }

    private async Task<string> SendWhatsAppMessageAsync(RenderedNotification notification, CancellationToken ct)
    {
        var payload = new
        {
            chatId = FormatChatId(notification.RecipientAddress),
            text = notification.Body,
            session = _options.Session
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var url = $"{_options.BaseUrl.TrimEnd('/')}/api/sendText";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = jsonContent
        };

        AddAuthHeader(request);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        using var response = await _httpClient.SendAsync(request, timeoutCts.Token);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"WAHA API returned {(int)response.StatusCode}: {errorBody}");
        }

        var responseBody = await response.Content.ReadFromJsonAsync<WahaSendResponse>(cancellationToken: ct);
        return responseBody?.Id ?? $"wa-{Guid.NewGuid():N}";
    }

    private void AddAuthHeader(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-Api-Key", _options.ApiKey);
        }
    }

    /// <summary>
    /// Formats a phone number into a WAHA chat ID.
    /// WAHA expects format: "{phone}@c.us" for individual chats.
    /// </summary>
    private static string FormatChatId(string recipientAddress)
    {
        // Strip any non-numeric characters and append @c.us suffix if not already present
        if (recipientAddress.EndsWith("@c.us", StringComparison.OrdinalIgnoreCase))
            return recipientAddress;

        var numericOnly = new string(recipientAddress.Where(char.IsDigit).ToArray());
        return $"{numericOnly}@c.us";
    }

    private sealed record WahaSendResponse(string? Id);
    private sealed record WahaSessionResponse(string? Status, string? Name);
}
