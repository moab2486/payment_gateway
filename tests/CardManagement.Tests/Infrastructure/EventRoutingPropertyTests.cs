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
/// Property-based tests for Event Routing Correctness (Property 1).
/// Validates: Requirements 3.1, 3.2, 3.3, 3.6, 5.1, 5.2, 5.3
/// </summary>
[Trait("Feature", "docker-kafka-integration")]
[Trait("Property", "1")]
public class EventRoutingPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.6, 5.1, 5.2, 5.3**
    ///
    /// Property 1: For any domain event (card-issued, transaction-authorized, or transaction-reversed)
    /// and any non-empty alphanumeric topic prefix, the event SHALL be published to the topic named
    /// {prefix}.{event-type-suffix}, where the event type maps deterministically from the event class.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property EventIsPublishedToCorrectTopic()
    {
        var gen = from prefix in GenTopicPrefix()
                  from eventCase in Gen.Choose(0, 2)
                  select (Prefix: prefix, EventCase: eventCase);

        return Prop.ForAll(gen.ToArbitrary(), testCase =>
        {
            var (prefix, eventCase) = testCase;
            var capturedTopics = new List<string>();
            var fakeProducer = new FakeProducer(capturedTopics);

            var options = Options.Create(new KafkaOptions
            {
                BootstrapServers = "localhost:9092",
                TopicPrefix = prefix,
                ClientId = "test-client"
            });

            var logger = NullLogger<KafkaEventPublisher>.Instance;
            using var publisher = new KafkaEventPublisher(fakeProducer, options, logger);

            string expectedTopic;
            switch (eventCase)
            {
                case 0:
                    expectedTopic = $"{prefix}.card-issued";
                    publisher.PublishCardIssuedAsync(CreateCardIssuedEvent(), CancellationToken.None).GetAwaiter().GetResult();
                    break;
                case 1:
                    expectedTopic = $"{prefix}.transaction-authorized";
                    publisher.PublishTransactionAuthorizedAsync(CreateTransactionAuthorizedEvent(), CancellationToken.None).GetAwaiter().GetResult();
                    break;
                default:
                    expectedTopic = $"{prefix}.transaction-reversed";
                    publisher.PublishTransactionReversedAsync(CreateTransactionReversedEvent(), CancellationToken.None).GetAwaiter().GetResult();
                    break;
            }

            return (capturedTopics.Count == 1 && capturedTopics[0] == expectedTopic)
                .Label($"Expected topic '{expectedTopic}' but got [{string.Join(", ", capturedTopics)}]");
        });
    }

    /// <summary>
    /// Generates a non-empty alphanumeric string suitable for use as a topic prefix.
    /// </summary>
    private static Gen<string> GenTopicPrefix()
    {
        return Gen.Choose(1, 20).SelectMany(length =>
            Gen.ArrayOf(length, Gen.Elements(
                "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray()))
            .Select(chars => new string(chars)));
    }

    private static CardIssuedEvent CreateCardIssuedEvent() => new()
    {
        CardId = Guid.NewGuid(),
        CardScheme = "Visa",
        AccountId = Guid.NewGuid(),
        IssuanceTimestamp = DateTime.UtcNow,
        CorrelationId = Guid.NewGuid().ToString()
    };

    private static TransactionAuthorizedEvent CreateTransactionAuthorizedEvent() => new()
    {
        TransactionId = Guid.NewGuid(),
        CardId = Guid.NewGuid(),
        Amount = 1000,
        Currency = "USD",
        ProcessorType = "CardFi",
        ResponseCode = "00",
        AuthorizationTimestamp = DateTime.UtcNow,
        CorrelationId = Guid.NewGuid().ToString()
    };

    private static TransactionReversedEvent CreateTransactionReversedEvent() => new()
    {
        OriginalTransactionId = Guid.NewGuid(),
        ReversalTransactionId = Guid.NewGuid(),
        Amount = 500,
        Currency = "USD",
        ReversalTimestamp = DateTime.UtcNow,
        CorrelationId = Guid.NewGuid().ToString()
    };

    /// <summary>
    /// Fake IProducer that captures the topic name from each ProduceAsync call.
    /// </summary>
    private sealed class FakeProducer : IProducer<string, string>
    {
        private readonly List<string> _capturedTopics;

        public FakeProducer(List<string> capturedTopics)
        {
            _capturedTopics = capturedTopics;
        }

        public Handle Handle => throw new NotImplementedException();
        public string Name => "fake-producer";

        public Task<DeliveryResult<string, string>> ProduceAsync(
            string topic,
            Message<string, string> message,
            CancellationToken cancellationToken = default)
        {
            _capturedTopics.Add(topic);
            return Task.FromResult(new DeliveryResult<string, string>
            {
                Topic = topic,
                Partition = new Partition(0),
                Offset = new Offset(0),
                Message = message
            });
        }

        public Task<DeliveryResult<string, string>> ProduceAsync(
            TopicPartition topicPartition,
            Message<string, string> message,
            CancellationToken cancellationToken = default)
        {
            _capturedTopics.Add(topicPartition.Topic);
            return Task.FromResult(new DeliveryResult<string, string>
            {
                Topic = topicPartition.Topic,
                Partition = topicPartition.Partition,
                Offset = new Offset(0),
                Message = message
            });
        }

        public void Produce(
            string topic,
            Message<string, string> message,
            Action<DeliveryReport<string, string>>? deliveryHandler = null)
        {
            _capturedTopics.Add(topic);
        }

        public void Produce(
            TopicPartition topicPartition,
            Message<string, string> message,
            Action<DeliveryReport<string, string>>? deliveryHandler = null)
        {
            _capturedTopics.Add(topicPartition.Topic);
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
