namespace CardManagement.Infrastructure.Durable;

/// <summary>
/// Abstraction for delays to enable unit testing without real waits.
/// </summary>
public interface IDelayStrategy
{
    Task DelayAsync(int milliseconds, CancellationToken ct);
}

/// <summary>
/// Production delay strategy using Task.Delay.
/// </summary>
public sealed class TaskDelayStrategy : IDelayStrategy
{
    public Task DelayAsync(int milliseconds, CancellationToken ct)
    {
        return Task.Delay(milliseconds, ct);
    }
}
