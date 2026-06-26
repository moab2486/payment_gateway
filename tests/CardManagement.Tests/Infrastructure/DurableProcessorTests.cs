using CardManagement.Infrastructure.Durable;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

public class DurableProcessorTests
{
    private static DurableProcessorOptions DefaultOptions(
        int maxRetries = 3,
        int initialRetryDelayMs = 10,
        string deadLetterTopicSuffix = ".dlq") => new()
    {
        ConsumerGroup = "test-group",
        Topics = new List<string> { "test-topic" },
        MaxRetries = maxRetries,
        InitialRetryDelayMs = initialRetryDelayMs,
        DeadLetterTopicSuffix = deadLetterTopicSuffix
    };

    private static DurableProcessor CreateProcessor(
        FakeKafkaConsumer consumer,
        FakeDeadLetterProducer dlqProducer,
        IMessageHandler handler,
        DurableProcessorOptions? options = null,
        IDelayStrategy? delayStrategy = null)
    {
        var opts = options ?? DefaultOptions();
        return new DurableProcessor(
            consumer,
            dlqProducer,
            handler,
            Options.Create(opts),
            NullLogger<DurableProcessor>.Instance,
            delayStrategy ?? new InstantDelayStrategy());
    }

    [Fact]
    public async Task SuccessfulProcessing_CommitsOffset()
    {
        // Arrange
        var consumer = new FakeKafkaConsumer();
        consumer.Enqueue("test-topic", "key1", "payload1", partition: 0, offset: 0);
        var dlqProducer = new FakeDeadLetterProducer();
        var handler = new FakeMessageHandler();

        var processor = CreateProcessor(consumer, dlqProducer, handler);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await processor.StartAsync(cts.Token);
        await Task.Delay(200, CancellationToken.None);
        await processor.StopAsync(CancellationToken.None);

        // Assert
        Assert.Single(handler.ProcessedMessages);
        Assert.Equal("payload1", handler.ProcessedMessages[0].Value);
        Assert.Single(consumer.CommittedResults);
        Assert.Empty(dlqProducer.PublishedMessages);
    }

    [Fact]
    public async Task TransientError_RetriesWithExponentialBackoff()
    {
        // Arrange
        var consumer = new FakeKafkaConsumer();
        consumer.Enqueue("test-topic", "key1", "payload1", partition: 0, offset: 0);
        var dlqProducer = new FakeDeadLetterProducer();
        var delayStrategy = new RecordingDelayStrategy();

        // Fail twice, then succeed
        var handler = new FakeMessageHandler(failCount: 2);

        var options = DefaultOptions(maxRetries: 5, initialRetryDelayMs: 100);
        var processor = CreateProcessor(consumer, dlqProducer, handler, options, delayStrategy);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await processor.StartAsync(cts.Token);
        await Task.Delay(300, CancellationToken.None);
        await processor.StopAsync(CancellationToken.None);

        // Assert - message was eventually processed successfully
        Assert.Equal(3, handler.Attempts); // 2 failures + 1 success
        Assert.Single(consumer.CommittedResults);
        Assert.Empty(dlqProducer.PublishedMessages);

        // Verify exponential backoff delays: 100ms, 200ms
        Assert.Equal(2, delayStrategy.Delays.Count);
        Assert.Equal(100, delayStrategy.Delays[0]);
        Assert.Equal(200, delayStrategy.Delays[1]);
    }

    [Fact]
    public async Task PermanentError_RoutesToDLQ()
    {
        // Arrange
        var consumer = new FakeKafkaConsumer();
        consumer.Enqueue("test-topic", "key1", "payload1", partition: 0, offset: 0);
        var dlqProducer = new FakeDeadLetterProducer();
        var handler = new FakeMessageHandler(permanentFailure: true);

        var processor = CreateProcessor(consumer, dlqProducer, handler);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await processor.StartAsync(cts.Token);
        await Task.Delay(200, CancellationToken.None);
        await processor.StopAsync(CancellationToken.None);

        // Assert
        Assert.Single(dlqProducer.PublishedMessages);
        Assert.Equal("test-topic", dlqProducer.PublishedMessages[0].OriginalTopic);
        Assert.Equal("key1", dlqProducer.PublishedMessages[0].Key);
        Assert.Equal("payload1", dlqProducer.PublishedMessages[0].Value);
        Assert.Single(consumer.CommittedResults); // Offset still committed after DLQ routing
    }

    [Fact]
    public async Task MaxRetriesExceeded_RoutesToDLQ()
    {
        // Arrange
        var consumer = new FakeKafkaConsumer();
        consumer.Enqueue("test-topic", "key1", "payload1", partition: 0, offset: 0);
        var dlqProducer = new FakeDeadLetterProducer();
        var handler = new FakeMessageHandler(failCount: 100); // Will always fail

        var options = DefaultOptions(maxRetries: 2, initialRetryDelayMs: 10);
        var processor = CreateProcessor(consumer, dlqProducer, handler, options);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await processor.StartAsync(cts.Token);
        await Task.Delay(300, CancellationToken.None);
        await processor.StopAsync(CancellationToken.None);

        // Assert - 1 initial attempt + 2 retries = 3 attempts
        Assert.Equal(3, handler.Attempts);
        Assert.Single(dlqProducer.PublishedMessages);
        Assert.Contains("Max retries exceeded", dlqProducer.PublishedMessages[0].Reason);
        Assert.Single(consumer.CommittedResults);
    }

    [Fact]
    public async Task PartitionOrdering_MaintainedBySequentialProcessing()
    {
        // Arrange
        var consumer = new FakeKafkaConsumer();
        consumer.Enqueue("test-topic", "key1", "msg1", partition: 0, offset: 0);
        consumer.Enqueue("test-topic", "key1", "msg2", partition: 0, offset: 1);
        consumer.Enqueue("test-topic", "key1", "msg3", partition: 0, offset: 2);
        var dlqProducer = new FakeDeadLetterProducer();
        var handler = new FakeMessageHandler();

        var processor = CreateProcessor(consumer, dlqProducer, handler);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await processor.StartAsync(cts.Token);
        await Task.Delay(500, CancellationToken.None);
        await processor.StopAsync(CancellationToken.None);

        // Assert - messages processed in partition order
        Assert.Equal(3, handler.ProcessedMessages.Count);
        Assert.Equal("msg1", handler.ProcessedMessages[0].Value);
        Assert.Equal("msg2", handler.ProcessedMessages[1].Value);
        Assert.Equal("msg3", handler.ProcessedMessages[2].Value);

        // Offsets committed in order
        Assert.Equal(3, consumer.CommittedResults.Count);
        Assert.Equal(0, consumer.CommittedResults[0].Offset.Value);
        Assert.Equal(1, consumer.CommittedResults[1].Offset.Value);
        Assert.Equal(2, consumer.CommittedResults[2].Offset.Value);
    }

    [Fact]
    public async Task CrashRecovery_ResumesFromLastCommittedOffset()
    {
        // Arrange - simulate consumer that starts from last committed offset
        var consumer = new FakeKafkaConsumer();
        // Messages starting from offset 5 (simulating crash recovery)
        consumer.Enqueue("test-topic", "key1", "msg-after-crash", partition: 0, offset: 5);
        var dlqProducer = new FakeDeadLetterProducer();
        var handler = new FakeMessageHandler();

        var processor = CreateProcessor(consumer, dlqProducer, handler);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await processor.StartAsync(cts.Token);
        await Task.Delay(200, CancellationToken.None);
        await processor.StopAsync(CancellationToken.None);

        // Assert - resumed from offset 5 (AutoOffsetReset = Earliest, manual commit)
        Assert.Single(handler.ProcessedMessages);
        Assert.Equal("msg-after-crash", handler.ProcessedMessages[0].Value);
        Assert.Single(consumer.CommittedResults);
        Assert.Equal(5, consumer.CommittedResults[0].Offset.Value);
    }

    [Fact]
    public async Task GracefulShutdown_CommitsFinalOffsets()
    {
        // Arrange
        var consumer = new FakeKafkaConsumer();
        consumer.Enqueue("test-topic", "key1", "payload1", partition: 0, offset: 0);
        var dlqProducer = new FakeDeadLetterProducer();
        var handler = new FakeMessageHandler();

        var processor = CreateProcessor(consumer, dlqProducer, handler);

        // Act - start and allow processing, then stop
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await processor.StartAsync(cts.Token);
        await Task.Delay(200, CancellationToken.None);
        await processor.StopAsync(CancellationToken.None);

        // Assert - message was processed and committed before shutdown
        Assert.Single(handler.ProcessedMessages);
        Assert.Single(consumer.CommittedResults);
        Assert.True(consumer.WasClosed);
    }

    [Fact]
    public async Task MultipleTopics_AllSubscribed()
    {
        // Arrange
        var consumer = new FakeKafkaConsumer();
        consumer.Enqueue("topic-a", "key1", "msg-a", partition: 0, offset: 0);
        consumer.Enqueue("topic-b", "key2", "msg-b", partition: 0, offset: 0);
        var dlqProducer = new FakeDeadLetterProducer();
        var handler = new FakeMessageHandler();

        var options = DefaultOptions();
        options.Topics = new List<string> { "topic-a", "topic-b" };
        var processor = CreateProcessor(consumer, dlqProducer, handler, options);

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await processor.StartAsync(cts.Token);
        await Task.Delay(300, CancellationToken.None);
        await processor.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, handler.ProcessedMessages.Count);
        Assert.Contains(consumer.SubscribedTopics, t => t == "topic-a");
        Assert.Contains(consumer.SubscribedTopics, t => t == "topic-b");
    }
}

#region Test Fakes

internal sealed class FakeKafkaConsumer : IKafkaConsumerWrapper
{
    private readonly Queue<ConsumeResult<string, string>> _messages = new();
    public List<ConsumeResult<string, string>> CommittedResults { get; } = new();
    public List<string> SubscribedTopics { get; } = new();
    public bool WasClosed { get; private set; }

    public void Enqueue(string topic, string key, string value, int partition, long offset)
    {
        _messages.Enqueue(new ConsumeResult<string, string>
        {
            Topic = topic,
            Partition = new Partition(partition),
            Offset = new Offset(offset),
            Message = new Message<string, string> { Key = key, Value = value }
        });
    }

    public void Subscribe(IEnumerable<string> topics)
    {
        SubscribedTopics.AddRange(topics);
    }

    public ConsumeResult<string, string>? Consume(CancellationToken ct)
    {
        if (_messages.Count > 0)
            return _messages.Dequeue();

        // Block until cancellation to simulate waiting for messages
        ct.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(100));
        ct.ThrowIfCancellationRequested();
        return null;
    }

    public void Commit(ConsumeResult<string, string> result)
    {
        CommittedResults.Add(result);
    }

    public void Close()
    {
        WasClosed = true;
    }

    public void Dispose()
    {
    }
}

internal sealed class FakeDeadLetterProducer : IDeadLetterProducer
{
    public List<DlqMessage> PublishedMessages { get; } = new();

    public Task PublishAsync(string originalTopic, string? key, string value, string reason, CancellationToken ct)
    {
        PublishedMessages.Add(new DlqMessage(originalTopic, key, value, reason));
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }

    public record DlqMessage(string OriginalTopic, string? Key, string Value, string Reason);
}

internal sealed class FakeMessageHandler : IMessageHandler
{
    private readonly int _failCount;
    private readonly bool _permanentFailure;
    private int _callCount;

    public List<ProcessedMessage> ProcessedMessages { get; } = new();
    public int Attempts => _callCount;

    public FakeMessageHandler(int failCount = 0, bool permanentFailure = false)
    {
        _failCount = failCount;
        _permanentFailure = permanentFailure;
    }

    public Task HandleAsync(string topic, string? key, string value, CancellationToken ct)
    {
        _callCount++;

        if (_permanentFailure)
            throw new PermanentErrorException("Permanent processing failure");

        if (_callCount <= _failCount)
            throw new InvalidOperationException($"Transient error (attempt {_callCount})");

        ProcessedMessages.Add(new ProcessedMessage(topic, key, value));
        return Task.CompletedTask;
    }

    public record ProcessedMessage(string Topic, string? Key, string Value);
}

internal sealed class InstantDelayStrategy : IDelayStrategy
{
    public Task DelayAsync(int milliseconds, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}

internal sealed class RecordingDelayStrategy : IDelayStrategy
{
    public List<int> Delays { get; } = new();

    public Task DelayAsync(int milliseconds, CancellationToken ct)
    {
        Delays.Add(milliseconds);
        return Task.CompletedTask;
    }
}

#endregion
