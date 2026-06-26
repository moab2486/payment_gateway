using System.Collections.Concurrent;
using CardManagement.Application.Ports;
using CardManagement.Infrastructure.Resilience;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.PlatformServices.Webhooks;

/// <summary>
/// Per-endpoint circuit breaker registry for webhook delivery.
/// Creates a dedicated circuit breaker for each unique destination URL to prevent
/// failed endpoints from affecting delivery to healthy ones.
/// </summary>
public sealed class WebhookCircuitBreakerRegistry : IWebhookCircuitBreakerRegistry
{
    private readonly ConcurrentDictionary<string, ICircuitBreaker> _breakers = new();
    private readonly CircuitBreakerOptions _options;

    public WebhookCircuitBreakerRegistry(IOptions<WebhookCircuitBreakerOptions> options)
    {
        _options = options?.Value?.ToCircuitBreakerOptions()
                   ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public ICircuitBreaker GetBreaker(string endpointUrl)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpointUrl);

        return _breakers.GetOrAdd(NormalizeUrl(endpointUrl), _ => new CircuitBreaker(_options));
    }

    /// <summary>
    /// Normalizes the URL to use as a consistent dictionary key (lowercase scheme + host).
    /// </summary>
    private static string NormalizeUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return $"{uri.Scheme}://{uri.Host}:{uri.Port}{uri.AbsolutePath}".ToLowerInvariant();
        }

        return url.ToLowerInvariant();
    }
}
