namespace CardManagement.Infrastructure.PlatformServices;

/// <summary>
/// Configuration options for a worker pool instance.
/// Bound from appsettings.json per-service sections.
/// </summary>
public class WorkerPoolOptions
{
    /// <summary>
    /// Number of concurrent workers consuming from the channel.
    /// </summary>
    public int Concurrency { get; set; } = 4;

    /// <summary>
    /// Maximum number of items the bounded channel can hold before backpressure is applied.
    /// When the channel is full, writers will wait until a slot becomes available.
    /// </summary>
    public int Capacity { get; set; } = 1000;
}
