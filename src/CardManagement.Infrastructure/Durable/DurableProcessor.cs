using CardManagement.Application.Ports;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Durable;

/// <summary>
/// Background service that consumes messages from Kafka with at-least-once delivery semantics.
/// Commits offsets only after successful processing, retries transient errors with exponential backoff,
/// and routes permanently failed messages to a dead-letter topic.
/// Maintains partition ordering by processing messages sequentially within each partition.
/// </summary>
public sealed class DurableProcessor : BackgroundService, IDurableProcessor
{
    private readonly IKafkaConsumerWrapper _consumer;
    private readonly IDeadLetterProducer _deadLetterProducer;
    private readonly IMessageHandler _messageHandler;
    private readonly DurableProcessorOptions _options;
    private readonly ILogger<DurableProcessor> _logger;
    private readonly IDelayStrategy _delayStrategy;

    public DurableProcessor(
        IKafkaConsumerWrapper consumer,
        IDeadLetterProducer deadLetterProducer,
        IMessageHandler messageHandler,
        IOptions<DurableProcessorOptions> options,
        ILogger<DurableProcessor> logger,
        IDelayStrategy? delayStrategy = null)
    {
        _consumer = consumer;
        _deadLetterProducer = deadLetterProducer;
        _messageHandler = messageHandler;
        _options = options.Value;
        _logger = logger;
        _delayStrategy = delayStrategy ?? new TaskDelayStrategy();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _consumer.Subscribe(_options.Topics);
        _logger.LogInformation(
            "DurableProcessor started. ConsumerGroup={ConsumerGroup}, Topics={Topics}",
            _options.ConsumerGroup, string.Join(", ", _options.Topics));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? consumeResult = null;

                try
                {
                    consumeResult = _consumer.Consume(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Error consuming message from Kafka");
                    continue;
                }

                if (consumeResult == null)
                    continue;

                await ProcessMessageWithRetryAsync(consumeResult, stoppingToken);
            }
        }
        finally
        {
            _consumer.Close();
            _logger.LogInformation("DurableProcessor stopped gracefully.");
        }
    }

    private async Task ProcessMessageWithRetryAsync(
        ConsumeResult<string, string> consumeResult,
        CancellationToken stoppingToken)
    {
        var topic = consumeResult.Topic;
        var key = consumeResult.Message.Key;
        var value = consumeResult.Message.Value;
        var retryCount = 0;

        while (true)
        {
            try
            {
                await _messageHandler.HandleAsync(topic, key, value, stoppingToken);

                // Processing succeeded — commit offset
                _consumer.Commit(consumeResult);

                _logger.LogDebug(
                    "Message processed and offset committed. Topic={Topic}, Partition={Partition}, Offset={Offset}",
                    topic, consumeResult.Partition.Value, consumeResult.Offset.Value);
                return;
            }
            catch (PermanentErrorException ex)
            {
                // Non-retryable error — route to DLQ
                _logger.LogWarning(ex,
                    "Permanent error processing message. Routing to DLQ. Topic={Topic}, Partition={Partition}, Offset={Offset}",
                    topic, consumeResult.Partition.Value, consumeResult.Offset.Value);

                try
                {
                    await _deadLetterProducer.PublishAsync(topic, key, value, ex.Message, stoppingToken);
                }
                catch (Exception dlqEx)
                {
                    _logger.LogError(dlqEx, "Failed to publish message to DLQ. Topic={Topic}", topic);
                }

                // Commit offset so we don't reprocess the permanently failed message
                _consumer.Commit(consumeResult);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutdown requested — do not commit, message will be reprocessed on restart
                _logger.LogInformation("Shutdown requested during message processing. Message will be reprocessed on restart.");
                return;
            }
            catch (Exception ex)
            {
                retryCount++;

                if (retryCount > _options.MaxRetries)
                {
                    // Exhausted retries — treat as permanent failure
                    _logger.LogError(ex,
                        "Max retries ({MaxRetries}) exceeded. Routing to DLQ. Topic={Topic}, Partition={Partition}, Offset={Offset}",
                        _options.MaxRetries, topic, consumeResult.Partition.Value, consumeResult.Offset.Value);

                    try
                    {
                        await _deadLetterProducer.PublishAsync(
                            topic, key, value,
                            $"Max retries exceeded. Last error: {ex.Message}",
                            stoppingToken);
                    }
                    catch (Exception dlqEx)
                    {
                        _logger.LogError(dlqEx, "Failed to publish message to DLQ. Topic={Topic}", topic);
                    }

                    _consumer.Commit(consumeResult);
                    return;
                }

                var delayMs = _options.InitialRetryDelayMs * (int)Math.Pow(2, retryCount - 1);
                _logger.LogWarning(ex,
                    "Transient error processing message. Retry {RetryCount}/{MaxRetries} after {DelayMs}ms. Topic={Topic}",
                    retryCount, _options.MaxRetries, delayMs, topic);

                await _delayStrategy.DelayAsync(delayMs, stoppingToken);
            }
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("DurableProcessor stopping. Processing in-flight messages before shutdown...");
        await base.StopAsync(cancellationToken);
    }
}
