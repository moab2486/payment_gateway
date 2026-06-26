namespace CardManagement.Infrastructure.Saga;

/// <summary>
/// Abstraction for time-based delays to support testability of exponential backoff.
/// </summary>
public interface IDelayProvider
{
    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}

/// <summary>
/// Production implementation that uses Task.Delay.
/// </summary>
public sealed class TaskDelayProvider : IDelayProvider
{
    public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
}
