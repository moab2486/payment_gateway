using System.Collections.Concurrent;
using CardManagement.Infrastructure.PlatformServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.PlatformServices.UnitTests.Infrastructure;

public class WorkerPoolTests
{
    private readonly ILogger _logger = NullLoggerFactory.Instance.CreateLogger("Test");

    [Fact]
    public async Task EnqueueAsync_ProcessesAllItems()
    {
        // Arrange
        var processed = new ConcurrentBag<int>();
        var options = new WorkerPoolOptions { Concurrency = 2, Capacity = 10 };
        var pool = new WorkerPool<int>(options, async (item, ct) =>
        {
            processed.Add(item);
            await Task.CompletedTask;
        }, _logger, "TestPool");

        using var cts = new CancellationTokenSource();

        // Act
        await pool.StartAsync(cts.Token);

        for (var i = 1; i <= 5; i++)
        {
            await pool.EnqueueAsync(i, cts.Token);
        }

        // Give workers time to process
        await Task.Delay(200);
        await cts.CancelAsync();
        await pool.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(5, processed.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, processed.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task WorkerPool_UsesConfiguredConcurrency()
    {
        // Arrange
        var maxConcurrent = 0;
        var currentConcurrent = 0;
        var lockObj = new object();
        var options = new WorkerPoolOptions { Concurrency = 3, Capacity = 50 };

        var pool = new WorkerPool<int>(options, async (item, ct) =>
        {
            var current = Interlocked.Increment(ref currentConcurrent);
            lock (lockObj)
            {
                if (current > maxConcurrent) maxConcurrent = current;
            }
            await Task.Delay(100, ct);
            Interlocked.Decrement(ref currentConcurrent);
        }, _logger, "ConcurrencyTest");

        using var cts = new CancellationTokenSource();

        // Act
        await pool.StartAsync(cts.Token);

        for (var i = 0; i < 20; i++)
        {
            await pool.EnqueueAsync(i, cts.Token);
        }

        // Wait for all to process
        await Task.Delay(1500);
        await cts.CancelAsync();
        await pool.StopAsync(CancellationToken.None);

        // Assert - max concurrency should not exceed configured value
        Assert.True(maxConcurrent <= 3, $"Max concurrent was {maxConcurrent}, expected <= 3");
        Assert.True(maxConcurrent >= 2, $"Max concurrent was {maxConcurrent}, expected >= 2 (workers should run in parallel)");
    }

    [Fact]
    public async Task WorkerPool_ContinuesProcessingAfterTaskError()
    {
        // Arrange
        var processed = new ConcurrentBag<int>();
        var options = new WorkerPoolOptions { Concurrency = 1, Capacity = 10 };

        var pool = new WorkerPool<int>(options, async (item, ct) =>
        {
            if (item == 3)
                throw new InvalidOperationException("Simulated failure");

            processed.Add(item);
            await Task.CompletedTask;
        }, _logger, "ErrorTestPool");

        using var cts = new CancellationTokenSource();

        // Act
        await pool.StartAsync(cts.Token);

        for (var i = 1; i <= 5; i++)
        {
            await pool.EnqueueAsync(i, cts.Token);
        }

        await Task.Delay(200);
        await cts.CancelAsync();
        await pool.StopAsync(CancellationToken.None);

        // Assert - item 3 should not be in processed (it threw), but 1, 2, 4, 5 should be
        Assert.DoesNotContain(3, processed);
        Assert.Contains(1, processed);
        Assert.Contains(2, processed);
        Assert.Contains(4, processed);
        Assert.Contains(5, processed);
    }

    [Fact]
    public async Task GracefulShutdown_DrainsRemainingItems()
    {
        // Arrange
        var processed = new ConcurrentBag<int>();
        var options = new WorkerPoolOptions { Concurrency = 1, Capacity = 100 };
        var gate = new TaskCompletionSource();

        var pool = new WorkerPool<int>(options, async (item, ct) =>
        {
            if (item == 1)
                await gate.Task; // Block first item
            processed.Add(item);
        }, _logger, "ShutdownTest");

        using var cts = new CancellationTokenSource();

        // Act
        await pool.StartAsync(cts.Token);

        // Enqueue items — first one blocks the worker
        for (var i = 1; i <= 5; i++)
        {
            await pool.EnqueueAsync(i, cts.Token);
        }

        // PendingTaskCount should be > 0 since worker is blocked
        await Task.Delay(50);
        Assert.True(pool.PendingTaskCount >= 1);

        // Release the gate and initiate graceful shutdown
        gate.SetResult();
        await Task.Delay(50);

        // Stop triggers CompleteWriter — workers drain remaining items
        await cts.CancelAsync();
        await pool.StopAsync(CancellationToken.None);

        // Assert - all items should have been processed before shutdown
        Assert.Equal(5, processed.Count);
    }

    [Fact]
    public void TryEnqueue_ReturnsFalseWhenFull()
    {
        // Arrange
        var options = new WorkerPoolOptions { Concurrency = 1, Capacity = 2 };

        // Use a handler that never completes (don't start the pool)
        var pool = new WorkerPool<int>(options, async (item, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
        }, _logger, "FullTest");

        // Act - fill the channel to capacity
        var first = pool.TryEnqueue(1);
        var second = pool.TryEnqueue(2);
        var third = pool.TryEnqueue(3); // Should fail - capacity is 2

        // Assert
        Assert.True(first);
        Assert.True(second);
        Assert.False(third);
    }

    [Fact]
    public void Constructor_ThrowsOnZeroConcurrency()
    {
        var options = new WorkerPoolOptions { Concurrency = 0, Capacity = 10 };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkerPool<int>(options, (_, _) => Task.CompletedTask, _logger));
    }

    [Fact]
    public void Constructor_ThrowsOnZeroCapacity()
    {
        var options = new WorkerPoolOptions { Concurrency = 1, Capacity = 0 };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkerPool<int>(options, (_, _) => Task.CompletedTask, _logger));
    }

    [Fact]
    public void Constructor_ThrowsOnNullHandler()
    {
        var options = new WorkerPoolOptions { Concurrency = 1, Capacity = 10 };

        Assert.Throws<ArgumentNullException>(() =>
            new WorkerPool<int>(options, null!, _logger));
    }

    [Fact]
    public void Constructor_ThrowsOnNullOptions()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WorkerPool<int>(null!, (_, _) => Task.CompletedTask, _logger));
    }

    [Fact]
    public async Task ActiveWorkerCount_ReflectsConfiguredConcurrency()
    {
        // Arrange
        var options = new WorkerPoolOptions { Concurrency = 4, Capacity = 10 };
        var pool = new WorkerPool<int>(options, async (_, ct) =>
        {
            await Task.Delay(50, ct);
        }, _logger, "ActiveCountTest");

        using var cts = new CancellationTokenSource();

        // Act
        await pool.StartAsync(cts.Token);
        await Task.Delay(50); // Let workers start

        // Assert
        Assert.Equal(4, pool.ActiveWorkerCount);

        await cts.CancelAsync();
        await pool.StopAsync(CancellationToken.None);
    }
}
