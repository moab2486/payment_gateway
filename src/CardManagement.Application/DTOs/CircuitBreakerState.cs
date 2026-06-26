namespace CardManagement.Application.DTOs;

/// <summary>
/// Represents the current state of a circuit breaker.
/// </summary>
public enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen
}
