using System.Collections.Concurrent;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.Enums;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Resilience;

/// <summary>
/// Maintains one <see cref="CircuitBreaker"/> per <see cref="PaymentChannel"/>.
/// Thread-safe lazy initialization ensures each channel gets exactly one breaker instance.
/// </summary>
public sealed class CircuitBreakerRegistry : ICircuitBreakerRegistryInternal
{
    private readonly ConcurrentDictionary<PaymentChannel, CircuitBreaker> _breakers = new();
    private readonly CircuitBreakerOptions _options;

    /// <inheritdoc />
    public event Action<PaymentChannel, CircuitBreakerState, CircuitBreakerState>? OnChannelStateChanged;

    public CircuitBreakerRegistry(IOptions<CircuitBreakerOptions> options)
    {
        _options = options.Value;
    }

    public ICircuitBreaker GetBreaker(PaymentChannel channel)
    {
        return _breakers.GetOrAdd(channel, ch =>
        {
            var breaker = new CircuitBreaker(_options);
            breaker.StateChanged += (prev, next) => OnChannelStateChanged?.Invoke(ch, prev, next);
            return breaker;
        });
    }

    public IReadOnlyDictionary<PaymentChannel, CircuitBreakerState> GetAllStates()
    {
        var result = new Dictionary<PaymentChannel, CircuitBreakerState>();
        foreach (PaymentChannel channel in Enum.GetValues<PaymentChannel>())
        {
            if (_breakers.TryGetValue(channel, out var breaker))
            {
                result[channel] = breaker.State;
            }
            else
            {
                result[channel] = CircuitBreakerState.Closed;
            }
        }
        return result;
    }
}
