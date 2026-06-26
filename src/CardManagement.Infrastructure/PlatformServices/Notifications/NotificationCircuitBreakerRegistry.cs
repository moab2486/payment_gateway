using System.Collections.Concurrent;
using CardManagement.Application.Ports;
using CardManagement.Infrastructure.Resilience;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.PlatformServices.Notifications;

/// <summary>
/// Per-channel circuit breaker registry for notification delivery.
/// Creates a dedicated circuit breaker for each notification channel (Email, SMS, WhatsApp)
/// to prevent a degraded provider from affecting other channels.
/// </summary>
public sealed class NotificationCircuitBreakerRegistry : INotificationCircuitBreakerRegistry
{
    private readonly ConcurrentDictionary<string, ICircuitBreaker> _breakers = new(StringComparer.OrdinalIgnoreCase);
    private readonly CircuitBreakerOptions _options;

    public NotificationCircuitBreakerRegistry(IOptions<NotificationCircuitBreakerOptions> options)
    {
        _options = options?.Value?.ToCircuitBreakerOptions()
                   ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public ICircuitBreaker GetBreaker(string channelName)
    {
        ArgumentException.ThrowIfNullOrEmpty(channelName);

        return _breakers.GetOrAdd(channelName, _ => new CircuitBreaker(_options));
    }
}
