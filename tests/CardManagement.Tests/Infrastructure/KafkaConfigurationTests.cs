using CardManagement.Application.DTOs;
using CardManagement.Infrastructure.Kafka;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Unit tests for NullEventPublisher, KafkaOptions defaults, and graceful degradation.
/// Validates: Requirements 4.2, 4.3, 4.4
/// </summary>
[Trait("Feature", "docker-kafka-integration")]
public class KafkaConfigurationTests
{
    #region NullEventPublisher Tests

    [Fact]
    public async Task PublishCardIssuedAsync_ReturnsCompletedTask()
    {
        var publisher = new NullEventPublisher(NullLogger<NullEventPublisher>.Instance);
        var cardEvent = new CardIssuedEvent
        {
            CardId = Guid.NewGuid(),
            CardScheme = "Visa",
            AccountId = Guid.NewGuid(),
            IssuanceTimestamp = DateTime.UtcNow,
            CorrelationId = "corr-001"
        };

        var task = publisher.PublishCardIssuedAsync(cardEvent, CancellationToken.None);

        Assert.True(task.IsCompleted);
        await task; // Should not throw
    }

    [Fact]
    public async Task PublishTransactionAuthorizedAsync_ReturnsCompletedTask()
    {
        var publisher = new NullEventPublisher(NullLogger<NullEventPublisher>.Instance);
        var txEvent = new TransactionAuthorizedEvent
        {
            TransactionId = Guid.NewGuid(),
            CardId = Guid.NewGuid(),
            Amount = 5000,
            Currency = "USD",
            ProcessorType = "CardFi",
            ResponseCode = "00",
            AuthorizationTimestamp = DateTime.UtcNow,
            CorrelationId = "corr-002"
        };

        var task = publisher.PublishTransactionAuthorizedAsync(txEvent, CancellationToken.None);

        Assert.True(task.IsCompleted);
        await task; // Should not throw
    }

    [Fact]
    public async Task PublishTransactionReversedAsync_ReturnsCompletedTask()
    {
        var publisher = new NullEventPublisher(NullLogger<NullEventPublisher>.Instance);
        var txEvent = new TransactionReversedEvent
        {
            OriginalTransactionId = Guid.NewGuid(),
            ReversalTransactionId = Guid.NewGuid(),
            Amount = 3000,
            Currency = "NGN",
            ReversalTimestamp = DateTime.UtcNow,
            CorrelationId = "corr-003"
        };

        var task = publisher.PublishTransactionReversedAsync(txEvent, CancellationToken.None);

        Assert.True(task.IsCompleted);
        await task; // Should not throw
    }

    #endregion

    #region KafkaOptions Defaults Tests

    [Fact]
    public void TopicPrefix_DefaultsTo_CardmgmtEvents()
    {
        var options = new KafkaOptions();
        Assert.Equal("cardmgmt.events", options.TopicPrefix);
    }

    [Fact]
    public void ClientId_DefaultsTo_CardManagementApi()
    {
        var options = new KafkaOptions();
        Assert.Equal("card-management-api", options.ClientId);
    }

    [Fact]
    public void BootstrapServers_DefaultsTo_EmptyString()
    {
        var options = new KafkaOptions();
        Assert.Equal(string.Empty, options.BootstrapServers);
    }

    #endregion

    #region Graceful Degradation Tests

    [Fact]
    public async Task PublishCardIssuedAsync_WhenProducerThrowsKafkaException_DoesNotPropagate()
    {
        var fakeProducer = new FakeThrowingProducer();
        var options = Options.Create(new KafkaOptions
        {
            BootstrapServers = "localhost:9092",
            TopicPrefix = "cardmgmt.events",
            ClientId = "test-client"
        });
        var logger = NullLogger<KafkaEventPublisher>.Instance;

        var publisher = new KafkaEventPublisher(fakeProducer, options, logger);

        var cardEvent = new CardIssuedEvent
        {
            CardId = Guid.NewGuid(),
            CardScheme = "Mastercard",
            AccountId = Guid.NewGuid(),
            IssuanceTimestamp = DateTime.UtcNow,
            CorrelationId = "corr-degrade-001"
        };

        // Should NOT throw even though the producer throws KafkaException
        var exception = await Record.ExceptionAsync(
            () => publisher.PublishCardIssuedAsync(cardEvent, CancellationToken.None));

        Assert.Null(exception);
    }

    #endregion

    #region Test Helpers

    /// <summary>
    /// Fake IProducer that throws KafkaException on ProduceAsync to simulate Kafka unavailability.
    /// </summary>
    private sealed class FakeThrowingProducer : IProducer<string, string>
    {
        public Handle Handle => throw new NotImplementedException();
        public string Name => "fake-throwing-producer";

        public Task<DeliveryResult<string, string>> ProduceAsync(
            string topic,
            Message<string, string> message,
            CancellationToken cancellationToken = default)
        {
            throw new KafkaException(new Error(ErrorCode.BrokerNotAvailable, "Simulated broker unavailability"));
        }

        public Task<DeliveryResult<string, string>> ProduceAsync(
            TopicPartition topicPartition,
            Message<string, string> message,
            CancellationToken cancellationToken = default)
        {
            throw new KafkaException(new Error(ErrorCode.BrokerNotAvailable, "Simulated broker unavailability"));
        }

        public void Produce(
            string topic,
            Message<string, string> message,
            Action<DeliveryReport<string, string>>? deliveryHandler = null)
        {
            throw new KafkaException(new Error(ErrorCode.BrokerNotAvailable, "Simulated broker unavailability"));
        }

        public void Produce(
            TopicPartition topicPartition,
            Message<string, string> message,
            Action<DeliveryReport<string, string>>? deliveryHandler = null)
        {
            throw new KafkaException(new Error(ErrorCode.BrokerNotAvailable, "Simulated broker unavailability"));
        }

        public int Poll(TimeSpan timeout) => 0;

        public int Flush(TimeSpan timeout) => 0;

        public void Flush(CancellationToken cancellationToken = default) { }

        public void InitTransactions(TimeSpan timeout) => throw new NotImplementedException();

        public void BeginTransaction() => throw new NotImplementedException();

        public void CommitTransaction(TimeSpan timeout) => throw new NotImplementedException();

        public void CommitTransaction() => throw new NotImplementedException();

        public void AbortTransaction(TimeSpan timeout) => throw new NotImplementedException();

        public void AbortTransaction() => throw new NotImplementedException();

        public void SetSaslCredentials(string username, string password) { }

        public void SendOffsetsToTransaction(
            IEnumerable<TopicPartitionOffset> offsets,
            IConsumerGroupMetadata groupMetadata,
            TimeSpan timeout) => throw new NotImplementedException();

        public int AddBrokers(string brokers) => 0;

        public void Dispose() { }
    }

    #endregion
}
