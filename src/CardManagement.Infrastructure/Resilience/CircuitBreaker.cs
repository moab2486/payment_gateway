using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;

namespace CardManagement.Infrastructure.Resilience;

/// <summary>
/// Thread-safe circuit breaker implementation with configurable thresholds.
/// States: Closed (normal), Open (rejecting), HalfOpen (probing).
/// </summary>
public sealed class CircuitBreaker : ICircuitBreaker
{
    private readonly object _lock = new();
    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;
    private readonly int _halfOpenProbeCount;

    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _consecutiveFailures;
    private int _halfOpenSuccesses;
    private DateTime _openedAtUtc = DateTime.MinValue;

    /// <summary>
    /// Event raised when the circuit breaker state changes.
    /// </summary>
    public event Action<CircuitBreakerState, CircuitBreakerState>? StateChanged;

    public CircuitBreaker(CircuitBreakerOptions options)
    {
        _failureThreshold = options.FailureThreshold;
        _openDuration = TimeSpan.FromSeconds(options.OpenDurationSeconds);
        _halfOpenProbeCount = options.HalfOpenProbeCount;
    }

    /// <summary>
    /// Internal constructor for testing with a custom time provider.
    /// </summary>
    public CircuitBreaker(CircuitBreakerOptions options, Func<DateTime> utcNowProvider)
        : this(options)
    {
        _utcNowProvider = utcNowProvider;
    }

    private Func<DateTime>? _utcNowProvider;
    private DateTime UtcNow => _utcNowProvider?.Invoke() ?? DateTime.UtcNow;

    public CircuitBreakerState State
    {
        get
        {
            lock (_lock)
            {
                if (_state == CircuitBreakerState.Open && UtcNow - _openedAtUtc >= _openDuration)
                {
                    TransitionTo(CircuitBreakerState.HalfOpen);
                }
                return _state;
            }
        }
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        var currentState = State;

        if (currentState == CircuitBreakerState.Open)
        {
            throw new CircuitBreakerOpenException("Circuit breaker is open. Requests are being rejected.");
        }

        try
        {
            var result = await action(ct);
            RecordSuccess();
            return result;
        }
        catch (Exception)
        {
            RecordFailure();
            throw;
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            switch (_state)
            {
                case CircuitBreakerState.Closed:
                    _consecutiveFailures = 0;
                    break;

                case CircuitBreakerState.HalfOpen:
                    _halfOpenSuccesses++;
                    if (_halfOpenSuccesses >= _halfOpenProbeCount)
                    {
                        TransitionTo(CircuitBreakerState.Closed);
                        _consecutiveFailures = 0;
                        _halfOpenSuccesses = 0;
                    }
                    break;
            }
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            switch (_state)
            {
                case CircuitBreakerState.Closed:
                    _consecutiveFailures++;
                    if (_consecutiveFailures >= _failureThreshold)
                    {
                        TransitionTo(CircuitBreakerState.Open);
                        _openedAtUtc = UtcNow;
                    }
                    break;

                case CircuitBreakerState.HalfOpen:
                    TransitionTo(CircuitBreakerState.Open);
                    _openedAtUtc = UtcNow;
                    _halfOpenSuccesses = 0;
                    break;
            }
        }
    }

    private void TransitionTo(CircuitBreakerState newState)
    {
        var previousState = _state;
        _state = newState;
        StateChanged?.Invoke(previousState, newState);
    }
}
