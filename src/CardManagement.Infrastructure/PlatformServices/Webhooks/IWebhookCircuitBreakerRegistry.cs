using CardManagement.Application.Ports;

namespace CardManagement.Infrastructure.PlatformServices.Webhooks;

/// <summary>
/// Maintains one circuit breaker per webhook destination endpoint URL.
/// Used to avoid hammering unresponsive merchant endpoints during delivery.
/// </summary>
public interface IWebhookCircuitBreakerRegistry
{
    /// <summary>
    /// Gets or creates a circuit breaker for the given endpoint URL.
    /// </summary>
    ICircuitBreaker GetBreaker(string endpointUrl);
}
