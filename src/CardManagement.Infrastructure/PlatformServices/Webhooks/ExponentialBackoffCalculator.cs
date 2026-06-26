namespace CardManagement.Infrastructure.PlatformServices.Webhooks;

/// <summary>
/// Calculates retry delays using exponential backoff with configurable jitter (±20%).
/// Formula: delay = baseDelay * multiplier^(attempt - 1) * jitterFactor
/// where jitterFactor ∈ [0.8, 1.2]
/// </summary>
public sealed class ExponentialBackoffCalculator
{
    private readonly double _baseDelayMs;
    private readonly double _multiplier;
    private readonly double _jitterMin;
    private readonly double _jitterMax;

    /// <summary>
    /// Creates an exponential backoff calculator with ±20% jitter.
    /// </summary>
    /// <param name="baseDelayMs">Base delay in milliseconds.</param>
    /// <param name="multiplier">Backoff multiplier (e.g., 2.0).</param>
    public ExponentialBackoffCalculator(double baseDelayMs = 5000, double multiplier = 2.0)
        : this(baseDelayMs, multiplier, 0.8, 1.2)
    {
    }

    /// <summary>
    /// Creates an exponential backoff calculator with custom jitter bounds.
    /// </summary>
    internal ExponentialBackoffCalculator(double baseDelayMs, double multiplier, double jitterMin, double jitterMax)
    {
        if (baseDelayMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(baseDelayMs), "Base delay must be positive.");
        if (multiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(multiplier), "Multiplier must be positive.");
        if (jitterMin < 0 || jitterMax < jitterMin)
            throw new ArgumentException("Jitter bounds must be non-negative and min <= max.");

        _baseDelayMs = baseDelayMs;
        _multiplier = multiplier;
        _jitterMin = jitterMin;
        _jitterMax = jitterMax;
    }

    /// <summary>
    /// Computes the retry delay for the given attempt number.
    /// </summary>
    /// <param name="attempt">1-based attempt number.</param>
    /// <returns>A TimeSpan representing the backoff delay.</returns>
    public TimeSpan ComputeDelay(int attempt)
    {
        if (attempt < 1)
            throw new ArgumentOutOfRangeException(nameof(attempt), "Attempt must be >= 1.");

        var baseComputed = _baseDelayMs * Math.Pow(_multiplier, attempt - 1);
        var jitterFactor = _jitterMin + (Random.Shared.NextDouble() * (_jitterMax - _jitterMin));
        var delayMs = baseComputed * jitterFactor;

        return TimeSpan.FromMilliseconds(delayMs);
    }

    /// <summary>
    /// Computes the retry delay for the given attempt number using a deterministic jitter factor.
    /// Useful for testing.
    /// </summary>
    /// <param name="attempt">1-based attempt number.</param>
    /// <param name="jitterFactor">Fixed jitter factor in [jitterMin, jitterMax] range.</param>
    /// <returns>A TimeSpan representing the backoff delay.</returns>
    internal TimeSpan ComputeDelay(int attempt, double jitterFactor)
    {
        if (attempt < 1)
            throw new ArgumentOutOfRangeException(nameof(attempt), "Attempt must be >= 1.");

        var baseComputed = _baseDelayMs * Math.Pow(_multiplier, attempt - 1);
        var delayMs = baseComputed * jitterFactor;

        return TimeSpan.FromMilliseconds(delayMs);
    }
}
