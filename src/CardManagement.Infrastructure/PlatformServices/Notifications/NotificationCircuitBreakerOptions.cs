using CardManagement.Infrastructure.Resilience;

namespace CardManagement.Infrastructure.PlatformServices.Notifications;

/// <summary>
/// Configuration options for notification channel circuit breakers.
/// </summary>
public sealed class NotificationCircuitBreakerOptions
{
    public const string SectionName = "NotificationCircuitBreaker";

    /// <summary>
    /// Number of consecutive failures before the circuit breaker opens for a channel.
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
