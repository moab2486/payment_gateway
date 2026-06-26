using CardManagement.Application.DTOs;
using CardManagement.Infrastructure.Resilience;
using Xunit;

namespace CardManagement.PlatformServices.IntegrationTests;

/// <summary>
/// Integration tests for circuit breaker open/close behavior.
/// Tests: Consecutive failures → circuit opens → requests short-circuited → 
/// half-open probe → circuit closes.
/// Requirements: All (end-to-end validation)
/// </summary>
public class CircuitBreakerIntegrationTests
{
    private static CircuitBreakerOptions CreateTestOptions(
        int failureThreshold = 3,
        int openDurationSeconds = 1,
        int halfOpenProbeCount = 2)
    {
        return new CircuitBreakerOptions
        {
            FailureThreshold = failureThreshold,
            OpenDurationSeconds = openDurationSeconds,
            HalfOpenProbeCount = halfOpenProbeCount
        };
    }

    [Fact]
    public async Task CircuitBreaker_ConsecutiveFailures_Opens()
    {
        // Arrange
        var options = CreateTestOptions(failureThreshold: 3);
        var breaker = new CircuitBreaker(options);
        Assert.Equal(CircuitBreakerState.Closed, breaker.State);

        // Act: Simulate 3 consecutive failures
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await breaker.ExecuteAsync<bool>(async ct =>
                {
                    throw new HttpRequestException("Connection refused");
                }, CancellationToken.None);
            }
            catch (HttpRequestException) { }
        }

        // Assert: Circuit is now open
        Assert.Equal(CircuitBreakerState.Open, breaker.State);
    }

    [Fact]
    public async Task CircuitBreaker_Open_RejectsRequests()
    {
        // Arrange: Force circuit open
        var options = CreateTestOptions(failureThreshold: 2);
        var breaker = new CircuitBreaker(options);

        for (int i = 0; i < 2; i++)
        {
            try
            {
                await breaker.ExecuteAsync<bool>(async ct =>
                    throw new HttpRequestException("fail"), CancellationToken.None);
            }
            catch (HttpRequestException) { }
        }

        Assert.Equal(CircuitBreakerState.Open, breaker.State);

        // Act & Assert: Subsequent requests are rejected immediately
        await Assert.ThrowsAsync<CircuitBreakerOpenException>(async () =>
        {
            await breaker.ExecuteAsync<bool>(async ct => true, CancellationToken.None);
        });
    }

    [Fact]
    public async Task CircuitBreaker_OpenDurationExpires_TransitionsToHalfOpen()
    {
        // Arrange: Use a time provider to control time advancement
        var currentTime = DateTime.UtcNow;
        var options = CreateTestOptions(failureThreshold: 2, openDurationSeconds: 1);
        var breaker = new CircuitBreaker(options, () => currentTime);

        // Trip the breaker
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await breaker.ExecuteAsync<bool>(async ct =>
                    throw new HttpRequestException("fail"), CancellationToken.None);
            }
            catch (HttpRequestException) { }
        }

        Assert.Equal(CircuitBreakerState.Open, breaker.State);

        // Act: Advance time past the open duration
        currentTime = currentTime.AddSeconds(2);

        // Assert: Circuit transitions to HalfOpen
        Assert.Equal(CircuitBreakerState.HalfOpen, breaker.State);
    }

    [Fact]
    public async Task CircuitBreaker_HalfOpen_SuccessProbes_ClosesCircuit()
    {
        // Arrange: Trip circuit and advance past open duration
        var currentTime = DateTime.UtcNow;
        var options = CreateTestOptions(failureThreshold: 2, openDurationSeconds: 1, halfOpenProbeCount: 2);
        var breaker = new CircuitBreaker(options, () => currentTime);

        for (int i = 0; i < 2; i++)
        {
            try
            {
                await breaker.ExecuteAsync<bool>(async ct =>
                    throw new HttpRequestException("fail"), CancellationToken.None);
            }
            catch (HttpRequestException) { }
        }

        // Advance time to trigger half-open
        currentTime = currentTime.AddSeconds(2);
        Assert.Equal(CircuitBreakerState.HalfOpen, breaker.State);

        // Act: Execute successful probes
        await breaker.ExecuteAsync<bool>(async ct => true, CancellationToken.None);
        Assert.Equal(CircuitBreakerState.HalfOpen, breaker.State); // Still half-open after 1 probe

        await breaker.ExecuteAsync<bool>(async ct => true, CancellationToken.None);

        // Assert: After 2 successful probes, circuit closes
        Assert.Equal(CircuitBreakerState.Closed, breaker.State);
    }

    [Fact]
    public async Task CircuitBreaker_HalfOpen_FailureReopens()
    {
        // Arrange: Get to half-open state
        var currentTime = DateTime.UtcNow;
        var options = CreateTestOptions(failureThreshold: 2, openDurationSeconds: 1, halfOpenProbeCount: 3);
        var breaker = new CircuitBreaker(options, () => currentTime);

        for (int i = 0; i < 2; i++)
        {
            try
            {
                await breaker.ExecuteAsync<bool>(async ct =>
                    throw new HttpRequestException("fail"), CancellationToken.None);
            }
            catch (HttpRequestException) { }
        }

        currentTime = currentTime.AddSeconds(2);
        Assert.Equal(CircuitBreakerState.HalfOpen, breaker.State);

        // Act: Execute 1 success then 1 failure in half-open
        await breaker.ExecuteAsync<bool>(async ct => true, CancellationToken.None);

        try
        {
            await breaker.ExecuteAsync<bool>(async ct =>
                throw new HttpRequestException("still failing"), CancellationToken.None);
        }
        catch (HttpRequestException) { }

        // Assert: Circuit reopens on failure during half-open
        Assert.Equal(CircuitBreakerState.Open, breaker.State);
    }

    [Fact]
    public async Task CircuitBreaker_FullLifecycle_ClosedToOpenToHalfOpenToClosed()
    {
        // Arrange
        var currentTime = DateTime.UtcNow;
        var options = CreateTestOptions(failureThreshold: 3, openDurationSeconds: 2, halfOpenProbeCount: 2);
        var breaker = new CircuitBreaker(options, () => currentTime);

        // Phase 1: Circuit starts Closed
        Assert.Equal(CircuitBreakerState.Closed, breaker.State);

        // Phase 2: Consecutive failures → Open
        for (int i = 0; i < 3; i++)
        {
            try
            {
                await breaker.ExecuteAsync<bool>(async ct =>
                    throw new HttpRequestException("fail"), CancellationToken.None);
            }
            catch (HttpRequestException) { }
        }
        Assert.Equal(CircuitBreakerState.Open, breaker.State);

        // Phase 3: Time passes → HalfOpen
        currentTime = currentTime.AddSeconds(3);
        Assert.Equal(CircuitBreakerState.HalfOpen, breaker.State);

        // Phase 4: Successful probes → Closed
        await breaker.ExecuteAsync<bool>(async ct => true, CancellationToken.None);
        await breaker.ExecuteAsync<bool>(async ct => true, CancellationToken.None);
        Assert.Equal(CircuitBreakerState.Closed, breaker.State);

        // Phase 5: Can execute normally again
        var result = await breaker.ExecuteAsync(async ct => 42, CancellationToken.None);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task CircuitBreaker_SuccessResetsFailureCount()
    {
        // Arrange
        var options = CreateTestOptions(failureThreshold: 3);
        var breaker = new CircuitBreaker(options);

        // 2 failures (below threshold)
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await breaker.ExecuteAsync<bool>(async ct =>
                    throw new HttpRequestException("fail"), CancellationToken.None);
            }
            catch (HttpRequestException) { }
        }

        // 1 success resets the counter
        await breaker.ExecuteAsync<bool>(async ct => true, CancellationToken.None);

        // 2 more failures (still below threshold since counter was reset)
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await breaker.ExecuteAsync<bool>(async ct =>
                    throw new HttpRequestException("fail"), CancellationToken.None);
            }
            catch (HttpRequestException) { }
        }

        // Assert: Still closed (2 consecutive failures, not 4)
        Assert.Equal(CircuitBreakerState.Closed, breaker.State);
    }

    [Fact]
    public async Task CircuitBreaker_StateChangeEvent_Fires()
    {
        // Arrange
        var stateChanges = new List<(CircuitBreakerState From, CircuitBreakerState To)>();
        var options = CreateTestOptions(failureThreshold: 2);
        var breaker = new CircuitBreaker(options);
        breaker.StateChanged += (from, to) => stateChanges.Add((from, to));

        // Act: Trip the breaker
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await breaker.ExecuteAsync<bool>(async ct =>
                    throw new HttpRequestException("fail"), CancellationToken.None);
            }
            catch (HttpRequestException) { }
        }

        // Assert
        Assert.Single(stateChanges);
        Assert.Equal(CircuitBreakerState.Closed, stateChanges[0].From);
        Assert.Equal(CircuitBreakerState.Open, stateChanges[0].To);
    }
}
