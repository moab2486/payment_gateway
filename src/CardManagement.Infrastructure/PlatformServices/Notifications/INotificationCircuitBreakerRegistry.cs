using CardManagement.Application.Ports;

namespace CardManagement.Infrastructure.PlatformServices.Notifications;

/// <summary>
/// Maintains one circuit breaker per notification channel (Email, SMS, WhatsApp).
/// Prevents repeated calls to a degraded provider while it recovers.
/// </summary>
public interface INotificationCircuitBreakerRegistry
{
    /// <summary>
    /// Gets the circuit breaker for the specified channel name.
    /// </summary>
    ICircuitBreaker GetBreaker(string channelName);
}
