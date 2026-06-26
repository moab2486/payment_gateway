using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.PlatformServices;

/// <summary>
/// Generic worker pool that uses a bounded <see cref="Channel{T}"/> as an in-process queue
/// with configurable concurrency. Implements <see cref="BackgroundService"/> to participate
/// in the host lifecycle with graceful shutdown semantics.
/// </summary>
/// <typeparam name="TTask">The type of task item to process.</typeparam>
public class WorkerPool<TTask> : BackgroundService
{
    private readonly Channel<TTask> _channel;
    private readonly int _concurrency;
    private readonly Func<TTask, CancellationToken, Task> _handler;
    private readonly ILogger _logger;
    private readonly string _poolName;

    /// <summary>
    /// Initializes a new instance of <see cref="WorkerPool{TTask}"/>.
    /// </summary>
    /// <param name="options">Configuration options for concurrency and channel capacity.</param>
    /// <param name="handler">The async delegate invoked for each dequeued task.</param>
    /// <param name="logger">Logger for operational diagnostics.</param>
    /// <param name="poolName">A human-readable name for this pool instance (used in logging).</param>
    public WorkerPool(
        WorkerPoolOptions options,
        Func<TTask, CancellationToken, Task> handler,
        ILogger logger,
        string? poolName = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(logger);

        if (options.Concurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Concurrency must be greater than zero.");

        if (options.Capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Capacity must be greater than zero.");

        _concurrency = options.Concurrency;
        _handler = handler;
        _logger = logger;
        _poolName = poolName ?? typeof(TTask).Name;

        _channel = Channel.CreateBounded<TTask>(new BoundedChannelOptions(options.Capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        });
    }

    /// <summary>
    /// Gets the number of items currently waiting in the channel to be processed.
    /// </summary>
    public int PendingTaskCount => _channel.Reader.Count;

    /// <summary>
    /// Gets the number of workers currently active (set during execution).
    /// </summary>
    public int ActiveWorkerCount { get; private set; }

    /// <summary>
    /// Enqueues a task item for processing. If the channel is full, this method
    /// will asynchronously wait until capacity becomes available (backpressure).
    /// </summary>
    /// <param name="task">The task to enqueue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the item is written to the channel.</returns>
    public async ValueTask EnqueueAsync(TTask task, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(task, cancellationToken);
    }

    /// <summary>
    /// Attempts to enqueue a task item without waiting. Returns false if the channel is full.
    /// </summary>
    /// <param name="task">The task to enqueue.</param>
    /// <returns>True if the item was written; false if the channel is at capacity.</returns>
    public bool TryEnqueue(TTask task)
    {
        return _channel.Writer.TryWrite(task);
    }

    /// <summary>
    /// Signals the channel that no more items will be written. Existing items
    /// will continue to be processed, and workers will exit once the channel is drained.
    /// Used for graceful shutdown.
    /// </summary>
    public void CompleteWriter()
    {
        _channel.Writer.Complete();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "WorkerPool '{PoolName}' starting with {Concurrency} workers and capacity {Capacity}.",
            _poolName, _concurrency, _channel.Reader.Count);

        var workers = new Task[_concurrency];
        ActiveWorkerCount = _concurrency;

        for (var i = 0; i < _concurrency; i++)
        {
            var workerId = i;
            workers[i] = Task.Run(() => RunWorkerAsync(workerId, stoppingToken), stoppingToken);
        }

        // Wait for all workers to complete (they exit when channel is completed + drained, or cancellation)
        await Task.WhenAll(workers);
        ActiveWorkerCount = 0;

        _logger.LogInformation("WorkerPool '{PoolName}' all workers stopped.", _poolName);
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "WorkerPool '{PoolName}' stopping. Completing writer and draining pending items...",
            _poolName);

        // Signal no more items will arrive — workers will drain remaining items and exit
        CompleteWriter();

        await base.StopAsync(cancellationToken);
    }

    private async Task RunWorkerAsync(int workerId, CancellationToken stoppingToken)
    {
        _logger.LogDebug("WorkerPool '{PoolName}' worker {WorkerId} started.", _poolName, workerId);

        try
        {
            await foreach (var task in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await _handler(task, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogDebug(
                        "WorkerPool '{PoolName}' worker {WorkerId} cancelled during task processing.",
                        _poolName, workerId);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "WorkerPool '{PoolName}' worker {WorkerId} encountered an error processing task.",
                        _poolName, workerId);
                    // Continue processing next item — individual task failures don't stop the worker
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (ChannelClosedException)
        {
            // Channel was completed and drained — normal exit path
        }

        _logger.LogDebug("WorkerPool '{PoolName}' worker {WorkerId} stopped.", _poolName, workerId);
    }
}
