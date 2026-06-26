using CardManagement.Application.DTOs;
using CardManagement.Domain.Enums;

namespace CardManagement.Application.Ports;

/// <summary>
/// Registry that maintains one circuit breaker per payment channel and exposes their states.
/// </summary>
public interface ICircuitBreakerRegistry
{
    ICircuitBreaker GetBreaker(PaymentChannel channel);
    IReadOnlyDictionary<PaymentChannel, CircuitBreakerState> GetAllStates();
}
