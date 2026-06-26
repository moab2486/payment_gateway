using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.Enums;

namespace CardManagement.Infrastructure.Resilience;

/// <summary>
/// Internal interface extending <see cref="ICircuitBreakerRegistry"/> with state change events
/// for infrastructure-level consumers like the Kafka degradation publisher.
/// </summary>
public interface ICircuitBreakerRegistryInternal : ICircuitBreakerRegistry
{
    /// <summary>
    /// Raised when any channel's circuit breaker state changes.
    /// Parameters: channel, previousState, newState.
    /// </summary>
    event Action<PaymentChannel, CircuitBreakerState, CircuitBreakerState>? OnChannelStateChanged;
}
