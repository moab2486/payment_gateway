using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.Enums;
using CardManagement.Infrastructure.Resilience;
using Microsoft.Extensions.Options;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

public class CircuitBreakerTests
{
    private static CircuitBreakerOptions DefaultOptions(
        int failureThreshold = 3,
        int openDurationSeconds = 5,
        int halfOpenProbeCount = 2) => new()
    {
        FailureThreshold = failureThreshold,
        OpenDurationSeconds = openDurationSeconds,
        HalfOpenProbeCount = halfOpenProbeCount
    };

    private static CircuitBreaker CreateBreaker(
        CircuitBreakerOptions? options = null,
        Func<DateTime>? utcNowProvider = null)
    {
        var opts = options ?? DefaultOptions();
        return utcNowProvider != null
            ? new CircuitBreaker(opts, utcNowProvider)
            : new CircuitBreaker(opts);
    }

    [Fact]
    public void InitialState_IsClosed()
    {
        var breaker = CreateBreaker();
        Assert.Equal(CircuitBreakerState.Closed, breaker.State);
    }

    [Fact]
    public void ClosedToOpen_TransitionsAfterThreshold()
    {
        var breaker = CreateBreaker(DefaultOptions(failureThreshold: 3));

        breaker.RecordFailure();
        Assert.Equal(CircuitBreakerState.Closed, breaker.State);

        breaker.RecordFailure();
        Assert.Equal(CircuitBreakerState.Closed, breaker.State);

        breaker.RecordFailure();
        Assert.Equal(CircuitBreakerState.Open, breaker.State);
    }

    [Fact]
    public async Task OpenState_RejectsRequests()
    {
        var breaker = CreateBreaker(DefaultOptions(failureThreshold: 1));
        breaker.RecordFailure();

        Assert.Equal(CircuitBreakerState.Open, breaker.State);

        await Assert.ThrowsAsync<CircuitBreakerOpenException>(async () =>
            await breaker.ExecuteAsync<int>(_ => Task.FromResult(42), CancellationToken.None));
    }

    [Fact]
    public void OpenToHalfOpen_TransitionsAfterDuration()
    {
        var now = DateTime.UtcNow;
        var currentTime = now;
        var breaker = CreateBreaker(
            DefaultOptions(failureThreshold: 1, openDurationSeconds: 5),
            () => currentTime);

        breaker.RecordFailure();
        Assert.Equal(CircuitBreakerState.Open, breaker.State);

        // Advance time past the open duration
        currentTime = now.AddSeconds(6);
        Assert.Equal(CircuitBreakerState.HalfOpen, breaker.State);
    }

    [Fact]
    public void OpenToHalfOpen_DoesNotTransitionBeforeDuration()
    {
        var now = DateTime.UtcNow;
        var currentTime = now;
        var breaker = CreateBreaker(
            DefaultOptions(failureThreshold: 1, openDurationSeconds: 10),
            () => currentTime);

        breaker.RecordFailure();
        Assert.Equal(CircuitBreakerState.Open, breaker.State);

        // Only 5 seconds passed — still within open duration
        currentTime = now.AddSeconds(5);
        Assert.Equal(CircuitBreakerState.Open, breaker.State);
    }

    [Fact]
    public void HalfOpenToClosed_TransitionsOnSuccessfulProbes()
    {
        var now = DateTime.UtcNow;
        var currentTime = now;
        var breaker = CreateBreaker(
            DefaultOptions(failureThreshold: 1, openDurationSeconds: 5, halfOpenProbeCount: 2),
            () => currentTime);

        // Trip to Open
        breaker.RecordFailure();
        Assert.Equal(CircuitBreakerState.Open, breaker.State);

        // Move to HalfOpen
        currentTime = now.AddSeconds(6);
        Assert.Equal(CircuitBreakerState.HalfOpen, breaker.State);

        // First successful probe
        breaker.RecordSuccess();
        Assert.Equal(CircuitBreakerState.HalfOpen, breaker.State);

        // Second successful probe — should close
        breaker.RecordSuccess();
        Assert.Equal(CircuitBreakerState.Closed, breaker.State);
    }

    [Fact]
    public void HalfOpenToOpen_TransitionsOnFailure()
    {
        var now = DateTime.UtcNow;
        var currentTime = now;
        var breaker = CreateBreaker(
            DefaultOptions(failureThreshold: 1, openDurationSeconds: 5, halfOpenProbeCount: 3),
            () => currentTime);

        // Trip to Open
        breaker.RecordFailure();

        // Move to HalfOpen
        currentTime = now.AddSeconds(6);
        Assert.Equal(CircuitBreakerState.HalfOpen, breaker.State);

        // Fail in HalfOpen — should go back to Open
        breaker.RecordFailure();
        Assert.Equal(CircuitBreakerState.Open, breaker.State);
    }

    [Fact]
    public void SuccessInClosed_ResetsFailureCount()
    {
        var breaker = CreateBreaker(DefaultOptions(failureThreshold: 3));

        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordSuccess(); // Resets failures

        // Two more failures should not trip because counter was reset
        breaker.RecordFailure();
        breaker.RecordFailure();
        Assert.Equal(CircuitBreakerState.Closed, breaker.State);

        // Third failure since last reset trips it
        breaker.RecordFailure();
        Assert.Equal(CircuitBreakerState.Open, breaker.State);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsSuccessOnCompletion()
    {
        var breaker = CreateBreaker(DefaultOptions(failureThreshold: 3));

        // Record 2 failures first
        breaker.RecordFailure();
        breaker.RecordFailure();

        // Successful execute should reset
        var result = await breaker.ExecuteAsync<int>(_ => Task.FromResult(42), CancellationToken.None);
        Assert.Equal(42, result);
        Assert.Equal(CircuitBreakerState.Closed, breaker.State);
    }

    [Fact]
    public async Task ExecuteAsync_RecordsFailureOnException()
    {
        var breaker = CreateBreaker(DefaultOptions(failureThreshold: 1));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            breaker.ExecuteAsync<int>(_ => throw new InvalidOperationException("test"), CancellationToken.None));

        Assert.Equal(CircuitBreakerState.Open, breaker.State);
    }

    [Fact]
    public void StateChanged_EventFires()
    {
        var breaker = CreateBreaker(DefaultOptions(failureThreshold: 1));
        var transitions = new List<(CircuitBreakerState prev, CircuitBreakerState next)>();
        breaker.StateChanged += (prev, next) => transitions.Add((prev, next));

        breaker.RecordFailure();

        Assert.Single(transitions);
        Assert.Equal(CircuitBreakerState.Closed, transitions[0].prev);
        Assert.Equal(CircuitBreakerState.Open, transitions[0].next);
    }

    [Fact]
    public async Task ConcurrentAccess_IsThreadSafe()
    {
        var breaker = CreateBreaker(DefaultOptions(failureThreshold: 100));
        var tasks = new List<Task>();

        // Run 200 concurrent failures — should trip at exactly 100
        for (int i = 0; i < 200; i++)
        {
            tasks.Add(Task.Run(() => breaker.RecordFailure()));
        }

        await Task.WhenAll(tasks);

        // After 200 failures with threshold 100, must be Open
        Assert.Equal(CircuitBreakerState.Open, breaker.State);
    }

    [Fact]
    public async Task ConcurrentAccess_MixedOperations()
    {
        var breaker = CreateBreaker(DefaultOptions(failureThreshold: 50));
        var tasks = new List<Task>();

        // Mix of successes and failures concurrently
        for (int i = 0; i < 100; i++)
        {
            if (i % 2 == 0)
                tasks.Add(Task.Run(() => breaker.RecordFailure()));
            else
                tasks.Add(Task.Run(() => breaker.RecordSuccess()));
        }

        await Task.WhenAll(tasks);

        // State should be valid (Closed or Open), not corrupted
        var state = breaker.State;
        Assert.True(state == CircuitBreakerState.Closed || state == CircuitBreakerState.Open);
    }

    [Fact]
    public void Registry_ReturnsConsistentBreakerPerChannel()
    {
        var options = Options.Create(DefaultOptions());
        var registry = new CircuitBreakerRegistry(options);

        var breaker1 = registry.GetBreaker(PaymentChannel.NIP);
        var breaker2 = registry.GetBreaker(PaymentChannel.NIP);
        var breaker3 = registry.GetBreaker(PaymentChannel.Interswitch);

        Assert.Same(breaker1, breaker2);
        Assert.NotSame(breaker1, breaker3);
    }

    [Fact]
    public void Registry_GetAllStates_ReturnsAllChannels()
    {
        var options = Options.Create(DefaultOptions(failureThreshold: 1));
        var registry = new CircuitBreakerRegistry(options);

        // Trip NIP breaker
        var nipBreaker = registry.GetBreaker(PaymentChannel.NIP);
        nipBreaker.RecordFailure();

        var states = registry.GetAllStates();

        Assert.Equal(CircuitBreakerState.Open, states[PaymentChannel.NIP]);
        // Channels not yet accessed should show Closed
        Assert.Equal(CircuitBreakerState.Closed, states[PaymentChannel.Interswitch]);
    }

    [Fact]
    public void Registry_RaisesStateChangedEvent()
    {
        var options = Options.Create(DefaultOptions(failureThreshold: 1));
        var registry = new CircuitBreakerRegistry(options);
        var events = new List<(PaymentChannel channel, CircuitBreakerState prev, CircuitBreakerState next)>();

        registry.OnChannelStateChanged += (ch, prev, next) => events.Add((ch, prev, next));

        var breaker = registry.GetBreaker(PaymentChannel.NQR);
        breaker.RecordFailure();

        Assert.Single(events);
        Assert.Equal(PaymentChannel.NQR, events[0].channel);
        Assert.Equal(CircuitBreakerState.Closed, events[0].prev);
        Assert.Equal(CircuitBreakerState.Open, events[0].next);
    }
}
