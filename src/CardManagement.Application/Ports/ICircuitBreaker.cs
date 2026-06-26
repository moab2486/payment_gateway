using CardManagement.Application.DTOs;

namespace CardManagement.Application.Ports;

/// <summary>
/// Represents a circuit breaker instance for a specific channel.
/// Tracks failures and controls access to the channel when degraded.
/// </summary>
public interface ICircuitBreaker
{
    CircuitBreakerState State { get; }
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);
    void RecordSuccess();
    void RecordFailure();
}
