using CardManagement.Infrastructure.Resilience;

namespace CardManagement.Infrastructure.PlatformServices.Webhooks;

/// <summary>
/// Configuration options for webhook endpoint circuit breakers.
/// </summary>
public sealed class WebhookCircuitBreakerOptions
{
    public const string SectionName = "WebhookCircuitBreaker";

    /// <summary>
    /// Number of consecutive failures before the circuit breaker opens for an endpoint.
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Duration in seconds the circuit breaker stays open before transitioning to half-open.
    /// </summary>
    public int OpenDurationSeconds { get; set; } = 60;

    /// <summary>
    /// Number of successful probe requests required in half-open state to close the circuit.
    /// </summary>
    public int HalfOpenProbeCount { get; set; } = 2;

    /// <summary>
    /// Converts to the shared CircuitBreakerOptions used by the CircuitBreaker implementation.
    /// </summary>
    internal CircuitBreakerOptions ToCircuitBreakerOptions() => new()
    {
        FailureThreshold = FailureThreshold,
        OpenDurationSeconds = OpenDurationSeconds,
        HalfOpenProbeCount = HalfOpenProbeCount
    };
}
