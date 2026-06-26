using CardManagement.Application.DTOs;
using CardManagement.Infrastructure.Kafka;
using Confluent.Kafka;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Property-based tests for Message Key Assignment (Property 5).
/// Validates: Requirements 5.8
/// </summary>
[Trait("Feature", "docker-kafka-integration")]
[Trait("Property", "5")]
public class MessageKeyAssignmentPropertyTests
{
    private static KafkaEventPublisher CreatePublisher(CapturingProducer producer)
    {
        var options = Options.Create(new KafkaOptions
        {
            BootstrapServers = "localhost:9092",
            TopicPrefix = "cardmgmt.events",
            ClientId = "test-client"
        });
        var logger = NullLogger<KafkaEventPublisher>.Instance;
        return new KafkaEventPublisher(producer, options, logger);
    }

    /// <summary>
    /// **Validates: Requirements 5.8**
    ///
    /// For any card-issued event, the Kafka message key SHALL equal the card identifier.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CardIssuedEvent_Key_Equals_CardId()
    {
        var gen = from cardId in Arb.Generate<Guid>()
                  from accountId in Arb.Generate<Guid>()
                  from correlationId in Gen.Elements("corr-1", "corr-2", "corr-abc", "trace-xyz")
                  from scheme in Gen.Elements("Visa", "Mastercard", "Verve")
                  select new CardIssuedEvent
                  {
                      CardId = cardId,
                      AccountId = accountId,
                      CardScheme = scheme,
                      IssuanceTimestamp = DateTime.UtcNow,
                      CorrelationId = correlationId
                  };

        return Prop.ForAll(gen.ToArbitrary(), cardEvent =>
        {
            var producer = new CapturingProducer();
            var publisher = CreatePublisher(producer);

            publisher.PublishCardIssuedAsync(cardEvent, CancellationToken.None).GetAwaiter().GetResult();

            var capturedKey = producer.LastMessage?.Key;
            return (capturedKey == cardEvent.CardId.ToString())
                .Label($"Expected key '{cardEvent.CardId}' but got '{capturedKey}'");
        });
    }

    /// <summary>
    /// **Validates: Requirements 5.8**
    ///
    /// For any transaction-authorized event, the message key SHALL equal the transaction identifier.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TransactionAuthorizedEvent_Key_Equals_TransactionId()
    {
        var gen = from transactionId in Arb.Generate<Guid>()
                  from cardId in Arb.Generate<Guid>()
                  from amount in Gen.Choose(1, 999999).Select(a => (long)a)
                  from currency in Gen.Elements("USD", "EUR", "GBP", "NGN")
                  from processorType in Gen.Elements("CardFi", "Interswitch")
                  from responseCode in Gen.Elements("00", "51", "61")
                  from correlationId in Gen.Elements("corr-1", "corr-2", "corr-abc")
                  select new TransactionAuthorizedEvent
                  {
                      TransactionId = transactionId,
                      CardId = cardId,
                      Amount = amount,
                      Currency = currency,
                      ProcessorType = processorType,
                      ResponseCode = responseCode,
                      AuthorizationTimestamp = DateTime.UtcNow,
                      CorrelationId = correlationId
                  };

        return Prop.ForAll(gen.ToArbitrary(), txEvent =>
        {
            var producer = new CapturingProducer();
            var publisher = CreatePublisher(producer);

            publisher.PublishTransactionAuthorizedAsync(txEvent, CancellationToken.None).GetAwaiter().GetResult();

            var capturedKey = producer.LastMessage?.Key;
            return (capturedKey == txEvent.TransactionId.ToString())
                .Label($"Expected key '{txEvent.TransactionId}' but got '{capturedKey}'");
        });
    }

    /// <summary>
    /// **Validates: Requirements 5.8**
    ///
    /// For any transaction-reversed event, the message key SHALL equal the original transaction identifier.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property TransactionReversedEvent_Key_Equals_OriginalTransactionId()
    {
        var gen = from originalTxId in Arb.Generate<Guid>()
                  from reversalTxId in Arb.Generate<Guid>()
                  from amount in Gen.Choose(1, 999999).Select(a => (long)a)
                  from currency in Gen.Elements("USD", "EUR", "GBP", "NGN")
                  from correlationId in Gen.Elements("corr-1", "corr-2", "corr-abc")
                  select new TransactionReversedEvent
                  {
                      OriginalTransactionId = originalTxId,
                      ReversalTransactionId = reversalTxId,
                      Amount = amount,
                      Currency = currency,
                      ReversalTimestamp = DateTime.UtcNow,
                      CorrelationId = correlationId
                  };

        return Prop.ForAll(gen.ToArbitrary(), txEvent =>
        {
            var producer = new CapturingProducer();
            var publisher = CreatePublisher(producer);

            publisher.PublishTransactionReversedAsync(txEvent, CancellationToken.None).GetAwaiter().GetResult();

            var capturedKey = producer.LastMessage?.Key;
            return (capturedKey == txEvent.OriginalTransactionId.ToString())
                .Label($"Expected key '{txEvent.OriginalTransactionId}' but got '{capturedKey}'");
        });
    }

    /// <summary>
    /// A capturing IProducer that records the last produced message for assertion.
    /// </summary>
    private sealed class CapturingProducer : IProducer<string, string>
    {
        public Message<string, string>? LastMessage { get; private set; }

        public Handle Handle => throw new NotImplementedException();
        public string Name => "test-producer";

        public Task<DeliveryResult<string, string>> ProduceAsync(
            string topic,
            Message<string, string> message,
            CancellationToken cancellationToken = default)
        {
            LastMessage = message;
            return Task.FromResult(new DeliveryResult<string, string>
            {
                Message = message,
                Topic = topic,
                Partition = new Partition(0),
                Offset = new Offset(0),
                Status = PersistenceStatus.Persisted
            });
        }

        public void Produce(
            string topic,
            Message<string, string> message,
            Action<DeliveryReport<string, string>>? deliveryHandler = null)
        {
            LastMessage = message;
        }

        public Task<DeliveryResult<string, string>> ProduceAsync(
            TopicPartition topicPartition,
            Message<string, string> message,
            CancellationToken cancellationToken = default)
        {
            LastMessage = message;
            return Task.FromResult(new DeliveryResult<string, string>
            {
                Message = message,
                Topic = topicPartition.Topic,
                Partition = topicPartition.Partition,
                Status = PersistenceStatus.Persisted
            });
        }

        public void Produce(
            TopicPartition topicPartition,
            Message<string, string> message,
            Action<DeliveryReport<string, string>>? deliveryHandler = null)
        {
            LastMessage = message;
        }

        public int Poll(TimeSpan timeout) => 0;
        public int Flush(TimeSpan timeout) => 0;
        public void Flush(CancellationToken cancellationToken = default) { }
        public void InitTransactions(TimeSpan timeout) { }
        public void BeginTransaction() { }
        public void CommitTransaction(TimeSpan timeout) { }
        public void CommitTransaction() { }
        public void AbortTransaction(TimeSpan timeout) { }
        public void AbortTransaction() { }
        public void SendOffsetsToTransaction(IEnumerable<TopicPartitionOffset> offsets, IConsumerGroupMetadata groupMetadata, TimeSpan timeout) { }
        public int AddBrokers(string brokers) => 0;
        public void SetSaslCredentials(string username, string password) { }
        public void Dispose() { }
    }
}
