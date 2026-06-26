using CardManagement.Infrastructure.PlatformServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.PlatformServices.IntegrationTests;

/// <summary>
/// Integration tests for background worker pool lifecycle: start, process, graceful shutdown.
/// Validates the WorkerPool starts workers, processes enqueued tasks, and drains cleanly on stop.
/// Requirements: 1.5, 5.1, 7.5
/// </summary>
public class WorkerPoolLifecycleTests
{
    [Fact]
    public async Task WorkerPool_Start_WorkersBeginProcessing()
    {
        // Arrange
        var processedItems = new List<int>();
        var options = new WorkerPoolOptions { Concurrency = 2, Capacity = 10 };
        var pool = new WorkerPool<int>(
            options,
            async (item, ct) =>
            {
                lock (processedItems)
                {
                    processedItems.Add(item);
                }
                await Task.Delay(10, ct);
            },
            NullLogger<WorkerPool<int>>.Instance,
            "TestPool");

        // Act: Start via hosted service pattern
        using var cts = new CancellationTokenSource();
        var executeTask = StartPoolAsync(pool, cts.Token);

        // Give workers time to start
        await Task.Delay(100);

        // Enqueue items
        for (int i = 1; i <= 5; i++)
        {
            await pool.EnqueueAsync(i, CancellationToken.None);
        }

        // Wait for processing
        await Task.Delay(500);

        // Assert: All items were processed
        Assert.Equal(5, processedItems.Count);
        Assert.Contains(1, processedItems);
        Assert.Contains(5, processedItems);

        // Cleanup
        cts.Cancel();
        await IgnoreCancellation(executeTask);
    }

    [Fact]
    public async Task WorkerPool_GracefulShutdown_DrainsRemainingItems()
    {
        // Arrange
        var processedItems = new List<int>();
        var options = new WorkerPoolOptions { Concurrency = 2, Capacity = 20 };
        var pool = new WorkerPool<int>(
            options,
            async (item, ct) =>
            {
                lock (processedItems)
                {
                    processedItems.Add(item);
                }
                await Task.Delay(20, ct);
            },
            NullLogger<WorkerPool<int>>.Instance,
            "ShutdownTestPool");

        using var cts = new CancellationTokenSource();
        var executeTask = StartPoolAsync(pool, cts.Token);
        await Task.Delay(100);

        // Enqueue items
        for (int i = 1; i <= 5; i++)
        {
            await pool.EnqueueAsync(i, CancellationToken.None);
        }

        // Act: Graceful shutdown — complete the writer so workers drain the queue
        await Task.Delay(200); // Let items start processing
        pool.CompleteWriter();

        // Wait for drain (with timeout)
        var timeout = Task.Delay(5000);
        await Task.WhenAny(executeTask, timeout);

        cts.Cancel();
        await IgnoreCancellation(executeTask);

        // Assert: All items were processed during drain
        Assert.Equal(5, processedItems.Count);
    }

    [Fact]
    public async Task WorkerPool_ConcurrentProcessing_MultipleWorkersActive()
    {
        // Arrange
        var concurrentCount = 0;
        var maxConcurrent = 0;
        var lockObj = new object();
        var options = new WorkerPoolOptions { Concurrency = 4, Capacity = 20 };

        var pool = new WorkerPool<int>(
            options,
            async (item, ct) =>
            {
                lock (lockObj)
                {
                    concurrentCount++;
                    maxConcurrent = Math.Max(maxConcurrent, concurrentCount);
                }
                await Task.Delay(200, ct);
                lock (lockObj)
                {
                    concurrentCount--;
                }
            },
            NullLogger<WorkerPool<int>>.Instance,
            "ConcurrencyTestPool");

        using var cts = new CancellationTokenSource();
        var executeTask = StartPoolAsync(pool, cts.Token);
        await Task.Delay(100);

        // Act: Enqueue enough items to saturate workers
        for (int i = 0; i < 8; i++)
        {
            await pool.EnqueueAsync(i, CancellationToken.None);
        }

        await Task.Delay(300); // Allow time for concurrent execution

        // Assert: Multiple workers executed concurrently
        Assert.True(maxConcurrent > 1, $"Expected concurrent execution but max was {maxConcurrent}");

        cts.Cancel();
        await IgnoreCancellation(executeTask);
    }

    [Fact]
    public async Task WorkerPool_TaskFailure_ContinuesProcessingNextItems()
    {
        // Arrange
        var processedItems = new List<int>();
        var options = new WorkerPoolOptions { Concurrency = 1, Capacity = 10 };

        var pool = new WorkerPool<int>(
            options,
            async (item, ct) =>
            {
                if (item == 3)
                    throw new InvalidOperationException("Simulated failure");

                lock (processedItems)
                {
                    processedItems.Add(item);
                }
                await Task.CompletedTask;
            },
            NullLogger<WorkerPool<int>>.Instance,
            "FailureTestPool");

        using var cts = new CancellationTokenSource();
        var executeTask = StartPoolAsync(pool, cts.Token);
        await Task.Delay(100);

        // Act: Enqueue items including one that will fail
        for (int i = 1; i <= 5; i++)
        {
            await pool.EnqueueAsync(i, CancellationToken.None);
        }

        await Task.Delay(500);

        // Assert: Items 1, 2, 4, 5 processed; item 3 failed but didn't stop the pool
        Assert.Equal(4, processedItems.Count);
        Assert.DoesNotContain(3, processedItems);
        Assert.Contains(1, processedItems);
        Assert.Contains(5, processedItems);

        cts.Cancel();
        await IgnoreCancellation(executeTask);
    }

    [Fact]
    public void WorkerPool_PendingTaskCount_ReflectsQueueDepth()
    {
        // Arrange
        var options = new WorkerPoolOptions { Concurrency = 1, Capacity = 10 };
        var pool = new WorkerPool<int>(
            options,
            async (item, ct) => await Task.Delay(1000, ct),
            NullLogger<WorkerPool<int>>.Instance,
            "QueueDepthPool");

        // Act: Enqueue without starting (items pile up)
        for (int i = 0; i < 5; i++)
        {
            pool.TryEnqueue(i);
        }

        // Assert
        Assert.Equal(5, pool.PendingTaskCount);
    }

    [Fact]
    public async Task WorkerPool_StopAsync_TriggersGracefulShutdown()
    {
        // Arrange
        var processedItems = new List<int>();
        var options = new WorkerPoolOptions { Concurrency = 2, Capacity = 10 };
        var pool = new WorkerPool<int>(
            options,
            async (item, ct) =>
            {
                lock (processedItems)
                {
                    processedItems.Add(item);
                }
                await Task.Delay(10, ct);
            },
            NullLogger<WorkerPool<int>>.Instance,
            "StopAsyncTestPool");

        using var cts = new CancellationTokenSource();
        var executeTask = StartPoolAsync(pool, cts.Token);
        await Task.Delay(100);

        // Enqueue some items
        for (int i = 1; i <= 3; i++)
        {
            await pool.EnqueueAsync(i, CancellationToken.None);
        }

        await Task.Delay(200);

        // Act: Stop the pool (simulates host shutdown)
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await pool.StopAsync(stopCts.Token);

        cts.Cancel();
        await IgnoreCancellation(executeTask);

        // Assert: Items were processed before shutdown
        Assert.Equal(3, processedItems.Count);
    }

    /// <summary>
    /// Starts the pool via the BackgroundService pattern using reflection to call ExecuteAsync.
    /// </summary>
    private static Task StartPoolAsync<TTask>(WorkerPool<TTask> pool, CancellationToken ct)
    {
        // BackgroundService.StartAsync calls ExecuteAsync internally
        return pool.StartAsync(ct);
    }

    private static async Task IgnoreCancellation(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Expected during test cleanup
        }
    }
}
