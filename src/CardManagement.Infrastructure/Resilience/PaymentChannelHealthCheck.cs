using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.Enums;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CardManagement.Infrastructure.Resilience;

/// <summary>
/// Reports the health status of payment channels based on their circuit breaker states.
/// Healthy: all channels are Closed or HalfOpen.
/// Degraded: some channels are Open but at least one is not.
/// Unhealthy: all channels are Open.
/// </summary>
public sealed class PaymentChannelHealthCheck : IHealthCheck
{
    private readonly ICircuitBreakerRegistry _registry;

    public PaymentChannelHealthCheck(ICircuitBreakerRegistry registry)
    {
        _registry = registry;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var states = _registry.GetAllStates();

        var data = new Dictionary<string, object>();
        foreach (var (channel, state) in states)
        {
            data[channel.ToString()] = state.ToString();
        }

        if (states.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy("No channels registered", data));
        }

        var allOpen = states.Values.All(s => s == CircuitBreakerState.Open);
        var anyOpen = states.Values.Any(s => s == CircuitBreakerState.Open);

        if (allOpen)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "All payment channels are unavailable (circuit breakers open)", exception: null, data: data));
        }

        if (anyOpen)
        {
            var openChannels = states
                .Where(kvp => kvp.Value == CircuitBreakerState.Open)
                .Select(kvp => kvp.Key.ToString());

            return Task.FromResult(HealthCheckResult.Degraded(
                $"Payment channels degraded: {string.Join(", ", openChannels)} circuit breaker(s) open", exception: null, data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "All payment channels operational", data));
    }
}
