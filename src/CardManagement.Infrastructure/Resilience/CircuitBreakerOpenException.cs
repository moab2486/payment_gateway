namespace CardManagement.Infrastructure.Resilience;

/// <summary>
/// Thrown when a request is rejected because the circuit breaker is in the Open state.
/// </summary>
public sealed class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message) { }
    public CircuitBreakerOpenException(string message, Exception innerException) : base(message, innerException) { }
}
