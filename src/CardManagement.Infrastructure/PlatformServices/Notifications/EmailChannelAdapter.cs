using System.Net;
using System.Net.Mail;
using CardManagement.Application.PlatformServices.Notifications.Ports;
using CardManagement.Domain.PlatformServices.Notifications;
using CardManagement.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.PlatformServices.Notifications;

/// <summary>
/// Email notification channel adapter using the configured SMTP provider.
/// Wraps SMTP send calls with a circuit breaker to avoid repeated attempts
/// against a degraded mail server.
/// </summary>
public sealed class EmailChannelAdapter : INotificationChannelAdapter
{
    private const string ChannelName = "Email";

    private readonly INotificationCircuitBreakerRegistry _circuitBreakerRegistry;
    private readonly EmailChannelOptions _options;
    private readonly ILogger<EmailChannelAdapter> _logger;

    public EmailChannelAdapter(
        INotificationCircuitBreakerRegistry circuitBreakerRegistry,
        IOptions<EmailChannelOptions> options,
        ILogger<EmailChannelAdapter> logger)
    {
        _circuitBreakerRegistry = circuitBreakerRegistry ?? throw new ArgumentNullException(nameof(circuitBreakerRegistry));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public NotificationChannel Channel => NotificationChannel.Email;

    /// <inheritdoc />
    public async Task<DeliveryResult> SendAsync(RenderedNotification notification, CancellationToken ct)
    {
        var breaker = _circuitBreakerRegistry.GetBreaker(ChannelName);

        try
        {
            var providerMessageId = await breaker.ExecuteAsync(async token =>
            {
                return await SendEmailAsync(notification, token);
            }, ct);

            _logger.LogInformation(
                "Email sent successfully to {Recipient}. MessageId={MessageId}",
                notification.RecipientAddress, providerMessageId);

            return DeliveryResult.Succeeded(providerMessageId);
        }
        catch (CircuitBreakerOpenException)
        {
            _logger.LogWarning(
                "Email circuit breaker is open. Cannot send to {Recipient}.",
                notification.RecipientAddress);

            return DeliveryResult.Failed("Email channel circuit breaker is open");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Propagate caller cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send email to {Recipient}: {Message}",
                notification.RecipientAddress, ex.Message);

            return DeliveryResult.Failed($"SMTP error: {ex.Message}");
        }
    }

    private async Task<string> SendEmailAsync(RenderedNotification notification, CancellationToken ct)
    {
        using var smtpClient = CreateSmtpClient();

        var mailMessage = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = notification.Subject ?? string.Empty,
            Body = notification.Body,
            IsBodyHtml = notification.Body.Contains('<')
        };

        mailMessage.To.Add(new MailAddress(notification.RecipientAddress));

        await smtpClient.SendMailAsync(mailMessage, ct);

        // SMTP doesn't return a message ID natively; generate a tracking ID
        var messageId = $"email-{Guid.NewGuid():N}";
        return messageId;
    }

    private SmtpClient CreateSmtpClient()
    {
        var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
        {
            EnableSsl = _options.UseTls,
            Timeout = _options.TimeoutSeconds * 1000
        };

        if (!string.IsNullOrEmpty(_options.Username))
        {
            client.Credentials = new NetworkCredential(_options.Username, _options.Password);
        }

        return client;
    }
}
