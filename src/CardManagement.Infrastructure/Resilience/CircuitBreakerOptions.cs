namespace CardManagement.Infrastructure.Resilience;

/// <summary>
/// Configuration options for the circuit breaker.
/// Loadable from appsettings via IOptions&lt;CircuitBreakerOptions&gt;.
/// </summary>
public sealed class CircuitBreakerOptions
{
    public const string SectionName = "CircuitBreaker";

    /// <summary>
    /// Number of consecutive failures before the circuit breaker opens.
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Duration in seconds the circuit breaker stays open before transitioning to half-open.
    /// </summary>
    public int OpenDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Number of successful probe requests required in half-open state to close the circuit.
    /// </summary>
    public int HalfOpenProbeCount { get; set; } = 3;
}
